using System;

namespace SampleShared;

internal interface IServer
{
    void Hi();
    void FancyHi(string name, int age);
    int Add(int x, int y);
    byte[] BufferCall(byte[] baseUtf8String, int n);
    (int a, int b, short c, byte[] utf8) GetValueTupleResult(string s);
    (uint a, long b, DateTime dt, double d)[] GetValueTupleArrayResult();
}
