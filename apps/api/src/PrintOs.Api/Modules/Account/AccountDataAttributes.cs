namespace PrintOs.Api.Modules.Account;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Property)]
public sealed class ExportExcludedAttribute(string reason) : Attribute
{
    public string Reason { get; } = reason;
}

[AttributeUsage(AttributeTargets.Property)]
public sealed class ExportAllowedAttribute(string reason) : Attribute
{
    public string Reason { get; } = reason;
}

[AttributeUsage(AttributeTargets.Property)]
public sealed class StoredFileKeyAttribute : Attribute;
