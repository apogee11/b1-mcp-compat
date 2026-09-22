# An unresolved mapping always fails the affected entity set

When neither the knowledge base nor the database catalog explains a property, the connector does not guess: that entity set returns a clear error naming the property, and an administrator adds an override. There is deliberately no setting to skip instead, to avoid configuration surface and the risk of silently missing a personal field. Other entity sets keep working.

## Considered Options

- A convention-based fallback (column named like the property): rejected, it would be wrong for properties such as Phone1 mapping to Tel1.
- A skip policy: rejected for now as extra moving parts.
