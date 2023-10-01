namespace TinyRpc;

[AttributeUsage(AttributeTargets.Class)]
public class TinyRpcClientClassAttribute : Attribute
{
    public TinyRpcClientClassAttribute(Type serverHandler) { }
}

[AttributeUsage(AttributeTargets.Class)]
public class TinyRpcServerClassAttribute : Attribute
{
    public TinyRpcServerClassAttribute(Type serverHandler) { }
}
