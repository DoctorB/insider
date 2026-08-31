## Insider - By hook or by crook##

**Insider** is a full-featured hooking library for managed code in Mono and Microsoft CLI enviroments.
Insider has support for both 32 and 64 bit systems, it does not use any kinds of injection but it applies hot
patch directly at runtime, due to the nature of its functionality don't use it in a production enviroments. *Insider* is working only with *managed code to managed domain*, it does not support any kind of hooking by managed code to unmanaged domain.

### InsiderManager class ###

**InsiderManager** class is responsible to hold your hooked methods and handling them properly. This class could be created with a singleton pattern at anytime and anywhere in your code.
Once a hook has been properly created, you can install it calling with `InsiderManger.Install("hook_name");` or simply uninstall by calling `InsiderManager.Uninstall("hook_name");` also if you want to remove it completly you can call `InsiderManager.Remove("hook_name");` and last if you want to remove every installed hooks you can call as well `InsiderManager.UninstallAll();`

### Different *Add* method for Mono and Microsoft CLI ###
You should use this *InsiderManager* method with *Mono*:

    Add(MethodInfo target, Delegate targetDelegate, MethodInfo hook, string name)

and this one with *Microsoft Framework*:

    Add(Delegate target, Delegate hook, string name)

### Unity example with instance method ###
**Insider** uses *unsafe* keyword in some methods, in order to be able to compile with your Unity, you should add the file *smcs.rsp* containing *-unsafe* in your Asset's project.

Let's say you have to hook some methods inside the *GameObject* class for your purposes.

      using UnityEngine;
      using System.Linq;
      using System;
      using Insider;

      public class Main : MonoBehaviour {

		// Create a singleton
	    private static readonly InsiderManager Manager = new InsiderManager();

		// We are going to hook GameObject.AddComponent<T>();
		// Just in this case, we can not hook directly the method above due to the nature
		// of how generic method works but we can hook the main method GameObject.AddComponent(Type componentType) and still get the same result...
	    private delegate Component InsiderAddComponent(Type componentType);

        // Use this for initialization
        void Start () {

		  // Some basic reflection here...
		  // Get the target method
          var srcMethod = typeof(GameObject).GetMethods()
            .First(m => m.Name == "AddComponent" && m.GetParameters().Length == 1 && m.GetParameters()[0].Name == "componentType");

          // Get your destination method
          var destMethod = typeof(Main).GetMethod("DoSomethingAddComponent");

          // Build up the specific delegate for the target method
		  var del = (InsiderAddComponent)Delegate.CreateDelegate(typeof(InsiderAddComponent), gameObject, srcMethod, true);

		  // And add it to the manager...
          Manager.Add(srcMethod,
             del,
             destMethod,
             "gameobject_addcomponent");

		  // Just call this to install your new hook...
          Manager.Install("gameobject_addcomponent");

          // Call this method anywhere and anytime in your code
		  gameObject.AddComponent<MyComponent1>();
		  gameObject.AddComponent<MyComponent2>();
		  gameObject.AddComponent<MyComponent3>();
        }

        public Component DoSomethingAddComponent(Type componentType)
        {
		  // Before to call the original method...
          Debug.Log(componentType.Name);

		  // Don't forget to call the original method
		  var res = Manager["gameobject_addcomponent"].CallOriginal(componentType);

          // After the original method has been called...
		  Debug.Log(res.GetType().FullName);

          return res as Component;
        }
      }

### Unity example with static method ###

    public class Main : MonoBehaviour
    {

      private static readonly InsiderManager Manager = new InsiderManager();
      private delegate GameObject InsiderFind(string name);

      // Use this for initialization
      void Start()
      {
        var srcMethod = typeof(GameObject).GetMethod("Find");
        var destMethod = typeof(Main).GetMethod("DoSomethingFind");
        var del = (InsiderFind)Delegate.CreateDelegate(typeof(InsiderFind), srcMethod, true);
        Manager.Add(srcMethod,
           del,
           destMethod,
           "gameobject_find");
        Manager.Install("gameobject_find");

        GameObject.Find("something");
      }

      public static GameObject DoSomethingFind(string name)
      {
        Debug.Log(name);
        return (GameObject)Manager["gameobject_find"].CallOriginal(name);
      }
    }
