using System.Text;
using System.Text.RegularExpressions;

namespace B1.Mcp.Compat.Core.Read;

public enum ReadLitKind { Str, Int, Dec, Date, Null }

/// <summary>A literal of a $filter or a key, as written (typing against the column happens when the SQL is built).</summary>
public sealed record ReadLit(ReadLitKind Kind, string Text);

public enum ReadLike { Contains, StartsWith, EndsWith }

public abstract record ReadFilterNode;
public sealed record ReadCmp(string Property, string Op, ReadLit Value) : ReadFilterNode;     // Op: eq ne lt le gt ge
public sealed record ReadLikeFn(string Property, ReadLike Mode, string Text) : ReadFilterNode;
public sealed record ReadNot(ReadFilterNode Inner) : ReadFilterNode;
public sealed record ReadLogic(string Op, List<ReadFilterNode> Items) : ReadFilterNode;       // Op: AND OR (same-operator chains are flattened)

/// <summary>
/// $filter parser. Grammar = what the native parser accepts (rea/18 2.1): operators and or eq ne le ge lt gt not, and the functions
/// contains, startswith, endswith. Everything else is a 400 with the native text.
/// </summary>
public static class ReadFilterParser
{
    private enum T { Ident, Str, Num, LParen, RParen, Comma, End }
    private sealed record Tok(T Kind, string Text);

    private static readonly Regex DateLit = new(@"^\d{4}-\d{2}-\d{2}(T\d{2}:\d{2}:\d{2}(\.\d+)?Z?)?$", RegexOptions.Compiled);
    private static readonly Regex NumLit = new(@"^-?\d+(\.\d+)?$", RegexOptions.Compiled);

    public static ReadException Invalid() => ReadException.Query("Query string error - Invalid symbol in the filter condition");
    public static ReadException NotSupportedFunction() => ReadException.Query("Query string error - Not supported function");

    public static ReadFilterNode Parse(string text)
    {
        var p = new Parser(Tokenize(text));
        var n = p.Or();
        if (p.Peek.Kind != T.End) throw Invalid();
        return n;
    }

    /// <summary>One literal (also used for key values in the URL).</summary>
    public static ReadLit ParseLiteral(string text)
    {
        var t = Tokenize(text);
        if (t.Count != 2 || t[0].Kind is not (T.Str or T.Num or T.Ident)) throw Invalid();
        return Literal(t[0]) ?? throw Invalid();
    }

    private static ReadLit? Literal(Tok t) => t.Kind switch
    {
        T.Str => new(ReadLitKind.Str, t.Text),
        T.Num when NumLit.IsMatch(t.Text) => new(t.Text.Contains('.') ? ReadLitKind.Dec : ReadLitKind.Int, t.Text),
        T.Num when DateLit.IsMatch(t.Text) => new(ReadLitKind.Date, t.Text),
        T.Ident when t.Text == "null" => new(ReadLitKind.Null, ""),
        _ => null,
    };

    private static List<Tok> Tokenize(string s)
    {
        var l = new List<Tok>();
        var i = 0;
        while (i < s.Length)
        {
            var c = s[i];
            if (char.IsWhiteSpace(c)) { i++; continue; }
            if (c == '(') { l.Add(new(T.LParen, "(")); i++; }
            else if (c == ')') { l.Add(new(T.RParen, ")")); i++; }
            else if (c == ',') { l.Add(new(T.Comma, ",")); i++; }
            else if (c == '\'')
            {
                var sb = new StringBuilder();
                i++;
                while (true)
                {
                    if (i >= s.Length) throw Invalid();
                    if (s[i] == '\'')
                    {
                        if (i + 1 < s.Length && s[i + 1] == '\'') { sb.Append('\''); i += 2; continue; }
                        i++;
                        break;
                    }
                    sb.Append(s[i++]);
                }
                l.Add(new(T.Str, sb.ToString()));
            }
            else if (char.IsDigit(c) || c == '-')
            {
                var st = i++;
                while (i < s.Length && (char.IsLetterOrDigit(s[i]) || s[i] is ':' or '-' or '.')) i++;
                l.Add(new(T.Num, s[st..i]));
            }
            else if (char.IsLetter(c) || c == '_')
            {
                var st = i++;
                while (i < s.Length && (char.IsLetterOrDigit(s[i]) || s[i] == '_')) i++;
                l.Add(new(T.Ident, s[st..i]));
            }
            else throw Invalid();
        }
        l.Add(new(T.End, ""));
        return l;
    }

    private sealed class Parser
    {
        private readonly List<Tok> _t;
        private int _i;
        public Parser(List<Tok> t) => _t = t;
        public Tok Peek => _t[_i];
        private Tok Next() => _t[_i++];
        private bool Word(string w) => Peek.Kind == T.Ident && Peek.Text == w;

        public ReadFilterNode Or() => Chain("or", "OR", And);
        private ReadFilterNode And() => Chain("and", "AND", Unary);

        private ReadFilterNode Chain(string word, string op, Func<ReadFilterNode> sub)
        {
            var first = sub();
            if (!Word(word)) return first;
            var items = new List<ReadFilterNode> { first };
            while (Word(word))
            {
                Next();
                items.Add(sub());
            }
            // a nested chain of the same operator is the same condition: flatten it
            return new ReadLogic(op, items.SelectMany(n => n is ReadLogic l && l.Op == op ? l.Items : new List<ReadFilterNode> { n }).ToList());
        }

        private ReadFilterNode Unary()
        {
            if (Word("not"))
            {
                Next();
                return new ReadNot(Unary());
            }
            if (Peek.Kind == T.LParen)
            {
                Next();
                var n = Or();
                if (Next().Kind != T.RParen) throw Invalid();
                return n;
            }
            if (Peek.Kind != T.Ident) throw Invalid();
            var name = Next().Text;
            if (Peek.Kind == T.LParen) return Function(name);
            var op = Peek.Kind == T.Ident ? Next().Text : throw Invalid();
            if (op == "in") throw NotSupportedFunction();
            if (op is not ("eq" or "ne" or "lt" or "le" or "gt" or "ge")) throw Invalid();
            var lit = Literal(Next()) ?? throw Invalid();
            return new ReadCmp(name, op, lit);
        }

        private ReadFilterNode Function(string name)
        {
            if (name == "substringof") throw ReadException.NotMeasured("$filter substringof (the native parser accepts it; its SQL was not measured)");
            if (name is not ("contains" or "startswith" or "endswith")) throw NotSupportedFunction();
            Next();   // (
            if (Peek.Kind != T.Ident) throw NotSupportedFunction();   // e.g. contains(tolower(x),'y')
            var prop = Next().Text;
            if (Peek.Kind == T.LParen) throw NotSupportedFunction();
            if (Next().Kind != T.Comma || Peek.Kind != T.Str) throw Invalid();
            var text = Next().Text;
            if (Next().Kind != T.RParen) throw Invalid();
            return new ReadLikeFn(prop, name == "contains" ? ReadLike.Contains : name == "startswith" ? ReadLike.StartsWith : ReadLike.EndsWith, text);
        }
    }
}
