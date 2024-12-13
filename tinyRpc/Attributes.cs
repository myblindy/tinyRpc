namespace TinyRpc;

#pragma warning disable CS9113 // Parameter is unread.

[AttributeUsage(AttributeTargets.Class)]
public class TinyRpcClientClassAttribute(Type serverHandler) : Attribute
{
}

[AttributeUsage(AttributeTargets.Class)]
public class TinyRpcServerClassAttribute(Type serverHandler) : Attribute
{
}
