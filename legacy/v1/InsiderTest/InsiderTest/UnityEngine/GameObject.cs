using System;

namespace InsiderTest.UnityEngine
{
  internal sealed class GameObject
  {
    public void SetActive(bool value)
    {
      Console.WriteLine("Original SetActive: " + value);
    }

    public Component AddComponent(Type componentType)
    {
      return new Component();
    }

    public T AddComponent<T>() where T : Component
    {
      Console.WriteLine("Original AddComponent<T>: " + typeof (T).FullName);
      return new Component() as T;
    }

    public Component AddComponent(string className)
    {
      return new Component();
    }

    public GameObject InstanceFind(string value)
    {
      Console.WriteLine("Original InstanceFind: " + value);
      return new GameObject();
    }

    public static GameObject Find(string value)
    {
      Console.WriteLine("Original Find: " + value);
      return new GameObject();
    }

    public static void Destroy(GameObject go)
    {
    }
  }
}
