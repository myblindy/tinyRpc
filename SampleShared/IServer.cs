using System;

namespace SampleShared;

enum E { A, B, C, D }

internal struct S11 { public int a; }
internal struct S1 { public int a; public string b { get; set; } public S11 S11; }

internal struct S22 { public int a; }
internal struct S2 { public string c; public ulong d; public S22 S22; }

internal interface IServer
{
    string ParamTest(int? a, E? e, S11? s11, (int? i, double? j, S11? s11)? tuple, S1?[] s1s);

    S2 GetStruct(int a, S1 s, double b);
    void Hi();
    void FancyHi(string name, int age);
    int Add(int x, int y);
    byte[] BufferCall(byte[] baseUtf8String, int n);
    (int a, int b, short c, byte[] utf8) GetValueTupleResult(string s);
    (uint a, long b, DateTime dt, double d)[] GetValueTupleArrayResult();
    E GetNewE(E input);
    double? GetNullableValue(float? val);
    byte[] GetLargeArray();

    string GetFastString();
    string GetSlowString();
    S11[] GetStructs(int a);

    event Action<double, string> OnData;
}
