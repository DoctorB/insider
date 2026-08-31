using System;
using System.Linq;

namespace Test
{
  internal class Program
  {
    private static void Main(string[] args)
    {
      var obj = new SomethingObj();

      var firstDel = (_SetSomething) Delegate.CreateDelegate(typeof (_SetSomething), obj, "SetSomething");

      var targetFind = typeof (SomethingObj).GetMethod("Find");
      var secondDel = (_Find) Delegate.CreateDelegate(typeof (_Find), obj, targetFind, true);

      var targetAdd = typeof (SomethingObj).GetMethods()
        .First(m => m.Name == "AddSomething" && m.GetParameters().Length == 0)
        .MakeGenericMethod(typeof (SomeStuffTest));
      var thirdDel =
        (_AddSomething<SomeStuffTest>)Delegate.CreateDelegate(typeof(_AddSomething<SomeStuffTest>), obj, targetAdd, true);


      var firstAddress = firstDel.Method.MethodHandle.GetFunctionPointer();
      var secondAddress = secondDel.Method.MethodHandle.GetFunctionPointer();
      var thirdAddress = thirdDel.Method.MethodHandle.GetFunctionPointer();

      // 32BIT
      //var instanceAddr = thirdAddress - 0x10;

      // 64BIT
      var instanceAddr = thirdAddress - 0x18;

      obj.SetSomething(true); // E8 XX XX XX XX calling exactly the first address got from the delegate
      firstDel.DynamicInvoke(true); // starts calling from the fp value
      var ret1 = obj.Find("something"); // E8 XX XX XX XX calling exactly the second address from the delegate
      var ret2 = secondDel.DynamicInvoke("something"); // starts calling from the fp value
      var ret3 = obj.AddSomething<SomeStuffTest>(); // E8 XX XX XX XX !!! Here the address does not match with the function pointer got from delegate
      var ret4 = thirdDel.DynamicInvoke(null); // starts calling from the fp value...

      Console.WriteLine("END");
      Console.ReadLine();

    }

    public class SomethingObj
    {
      public void SetSomething(bool value)
      {
      }

      public SomeStuff AddSomething(Type type)
      {
        return new SomeStuff();
      }

      public T AddSomething<T>() where T : SomeStuff
      {
        return new SomeStuff() as T;
      }

      public SomeStuff AddSomething(string className)
      {
        return new SomeStuff();
      }

      public SomeStuff Find(string value)
      {
        return new SomeStuff();
      }
    }

    public class SomeStuff
    {
      public int A { get; set; }
      public int B { get; set; }
      public SomeStuff()
      {
        A = 10;
        B = 10;
      }

      public void DoNothing()
      {

      }
    }

    public class SomeStuffTest : SomeStuff
    {
      public int C { get; set; }

      public SomeStuffTest() : base()
      {
        C = 10;
      }
    }

    private delegate void _SetSomething(bool value);

    private delegate SomeStuff _Find(string value);

    private delegate T _AddSomething<T>();
  }
}
