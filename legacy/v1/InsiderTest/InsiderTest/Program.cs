using System;
using System.Linq;
using System.Runtime.CompilerServices;
using Insider;
using InsiderTest.UnityEngine;

namespace InsiderTest
{
  internal class InsiderExample
  {
    private static readonly InsiderManager Manager = new InsiderManager();

    public InsiderExample()
    {
      #region SetActive

      // Just a small reflection here...
      var go = new GameObject();
      OriginalSetActive = (SetActive) Delegate.CreateDelegate(typeof (SetActive), go, "SetActive");
      // Pin up the delegate...
      HookedSetActive = DoSomethingSetActive;

      #endregion

      #region Find

      var targetFind = typeof (GameObject).GetMethod("Find");
      OriginalFind = (Find) Delegate.CreateDelegate(typeof (Find), targetFind);
      // Pin up the delegate...
      HookedFind = DoSomethingFind;

      #endregion

      #region InstanceFind

      var targetInstanceFind = typeof (GameObject).GetMethod("InstanceFind");
      OriginalInstanceFind = (InstanceFind) Delegate.CreateDelegate(typeof (InstanceFind), go, targetInstanceFind, true);
      // Pin up the delegate...
      HookedInstanceFind = DoSomethingInstanceFind;

      #endregion

      #region InsiderAddComponent<T>

      var targetAddComponent =
        typeof (GameObject).GetMethods()
          .First(m => m.Name == "AddComponent" && m.GetParameters().Length == 0)
          .MakeGenericMethod(typeof (ComponentTest));

      //RuntimeHelpers.PrepareMethod(targetAddComponent.MethodHandle);

      OriginalInsiderAdd =
        (InsiderAddComponent<ComponentTest>)
          Delegate.CreateDelegate(typeof(InsiderAddComponent<ComponentTest>), go, targetAddComponent, true);
      HookedInsiderAdd = DoSomethingAddComponent<ComponentTest>;

      #endregion

      // Add the specific hooks to the manager...
      Manager.Add(OriginalSetActive, HookedSetActive, "gameobject_setactive");
      //Manager.Add(OriginalFind, HookedFind, "gameobject_find");
      //Manager.Add(OriginalInsiderAdd, HookedInsiderAdd, "gameobject_addcomponent");

      // Install the specific hook from the manager
      // Start from now each call on hook method will be deviated to
      // your specific method...
      Manager.Install("gameobject_setactive");
      //Manager.Install("gameobject_find");
      //Manager.Install("gameobject_addcomponent");
      //Manager.Install("gameobject_instancefind");

      // se decommenti: cosi funziona anche l'hook sembra che il puntatore ricavato dal delegato in questo caso
      // faccia riferimento ad un clone del metodo e non a quello dell'istanza passata
      // OriginalInsiderAdd.DynamicInvoke(null);
      // OriginalInsiderAdd.Invoke(true);
    }

    // Pin-up to avoid garbage collector...
    private static SetActive OriginalSetActive { get; set; }
    private static SetActive HookedSetActive { get; set; }
    private static Find OriginalFind { get; set; }
    private static Find HookedFind { get; set; }
    private static InstanceFind OriginalInstanceFind { get; set; }
    private static InstanceFind HookedInstanceFind { get; set; }
    private static InsiderAddComponent<ComponentTest> OriginalInsiderAdd { get; set; }
    private static InsiderAddComponent<ComponentTest> HookedInsiderAdd { get; set; }
    // This method will be called on every GameObject.SetActive
    public void DoSomethingSetActive(bool value)
    {
      // Do whatever you want...
      Console.WriteLine("HookedSetActive DoSomethingSetActive: " + value);

      // and don't forget to call the original GameObject.SetActive
      Manager["gameobject_setactive"].CallOriginal(value);
    }

    // In this case the original Find is a static method
    public static GameObject DoSomethingFind(string value)
    {
      // Do whatever you want...
      Console.WriteLine("HookedFind DoSomethingFind: " + value);

      // and don't forget to call the original GameObject.Find
      return (GameObject) Manager["gameobject_find"].CallOriginal(value);
    }

    // In this case the original Find is an instance method
    public GameObject DoSomethingInstanceFind(string value)
    {
      // Do whatever you want...
      Console.WriteLine("HookedFind DoSomethingInstanceFind: " + value);

      // and don't forget to call the original GameObject.Find
      return (GameObject) Manager["gameobject_instancefind"].CallOriginal(value);
    }

    public T DoSomethingAddComponent<T>() where T : Component
    {
      // Do whatever you want...
      Console.WriteLine("HookedAddComponent DoSomethingAddComponent: " + typeof (T).FullName);

      // and don't forget to call the original GameObject.AddComponent<T>();
      return (T) Manager["gameobject_addcomponent"].CallOriginal(null);
    }

    public void RemoveHook(string hookName)
    {
      if (Manager.ContainsKey(hookName) && Manager[hookName].IsInstalled)
        Manager.Uninstall(hookName);
    }

    // This delegate signature must be the same of method you are going to hook
    // in this case SetActive is an instance method of the GameObject class
    private delegate void SetActive(bool value);

    private delegate GameObject Find(string value);

    private delegate GameObject InstanceFind(string value);

    private delegate T InsiderAddComponent<T>();
  }

  internal class Program
  {
    private static void Main(string[] args)
    {
      var insiderExample = new InsiderExample();
      var obj = new GameObject();

      #region Test with simple delegate and instance method

      Console.WriteLine("---");
      Console.WriteLine("Test with simple delegate and instance method");
      obj.SetActive(false);
      insiderExample.RemoveHook("gameobject_setactive");
      Console.WriteLine("Removing hook and calling set active again...");
      obj.SetActive(true);
      Console.WriteLine("---");

      #endregion

      Console.WriteLine("");

      #region Test with simple delegate and static method

      Console.WriteLine("---");
      Console.WriteLine("Test with simple delegate and static method");
      var ret = GameObject.Find("valuexyz");
      if (ret == null)
      {
        Console.WriteLine("ouch!");
      }
      insiderExample.RemoveHook("gameobject_find");
      Console.WriteLine("---");

      #endregion

      Console.WriteLine("");

      #region Test with complex delegate and instance method

      Console.WriteLine("---");
      Console.WriteLine("Test with complex delegate and instance method");
      obj.AddComponent<ComponentTest>();

      #endregion
      Console.ReadLine();
    }
  }
}
