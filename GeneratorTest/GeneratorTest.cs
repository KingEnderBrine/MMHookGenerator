using System;

namespace GeneratorTest
{
    public class GeneratorTest
    {
        public void Test()
        {
            On.Some.Namespace.TestClass.StaticMethod += TestClass_StaticMethod;
            IL.AnotherNamespace.ILTest.Method += ILTest_Method;

        }

        private void ILTest_Method(MonoMod.Cil.ILContext il)
        {
            throw new NotImplementedException();
        }

        private void TestClass_StaticMethod(Action<bool> arg1, bool arg2)
        {
            throw new NotImplementedException();
        }
    }
}
