using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace Insider
{
  /// <summary>
  ///   Manages all hooks.
  /// </summary>
  public class InsiderManager : Dictionary<string, Hook>
  {
    #region Public Methods and Operators

    /// <summary>
    ///   Adds a hook to the hook manager.
    /// </summary>
    /// <param name="target">
    ///   The target delegate method.
    /// </param>
    /// <param name="hook">
    ///   The hook delegate method.
    /// </param>
    /// <param name="name">
    ///   The name of the hook.
    /// </param>
    public void Add(Delegate target, Delegate hook, string name)
    {
      Add(name, new Hook(target, hook));
    }

    public void Add(MethodInfo target, Delegate targetDelegate, MethodInfo hook, string name)
    {
      Add(name, new Hook(target, hook, targetDelegate));
    }

    /// <summary>
    ///   Installs the hook of a given name.
    /// </summary>
    /// <param name="name">
    ///   The name of the hook to install.
    /// </param>
    /// <exception cref="HookNotFoundException">
    ///   Thrown if the named hook has not yet been added.
    /// </exception>
    /// <exception cref="HookInstallFailedException">
    ///   Thrown if the named hook fails to installs.
    /// </exception>
    public void Install(string name)
    {
      if (!ContainsKey(name))
      {
        throw new HookNotFoundException(
          "The hook " + name + " could not be found. Verify that you have added the hook " + name + ".");
      }

      if (!this[name].Install())
      {
        throw new HookInstallFailedException(
          "The hook " + name + " failed to install. Verify addresses are correct.");
      }
    }

    /// <summary>
    ///   Installs all hooks.
    /// </summary>
    /// <exception cref="HookInstallFailedException">
    ///   Thrown if the certain hook fails to installs.
    /// </exception>
    public void InstallAll()
    {
      foreach (var hook in this.Where(hook => !hook.Value.Install()))
      {
        throw new HookInstallFailedException(
          "The hook " + hook.Key + " failed to install. Verify addresses are correct.");
      }
    }

    /// <summary>
    ///   Deletes a hook from the hook manager.
    /// </summary>
    /// <param name="name">
    ///   The name of the hook.
    /// </param>
    public new void Remove(string name)
    {
      if (this[name].IsInstalled)
      {
        Uninstall(name);
      }

      base.Remove(name);
    }

    /// <summary>
    ///   Removes the hook of a given name.
    /// </summary>
    /// <param name="name">
    ///   The name of the hook to uninstall.
    /// </param>
    /// <exception cref="HookNotFoundException">
    ///   Thrown if the named hook has not yet been added.
    /// </exception>
    /// <exception cref="HookUninstallFailedException">
    ///   Thrown if the named hook fails to uninstall.
    /// </exception>
    public void Uninstall(string name)
    {
      if (!ContainsKey(name))
      {
        throw new HookNotFoundException(
          "The hook " + name + " could not be found. Verify that you have added the hook " + name + ".");
      }

      if (!this[name].Uninstall())
      {
        throw new HookUninstallFailedException(
          "The hook " + name + " failed to uninstall. Very addresses are correct.");
      }
    }

    /// <summary>
    ///   Uninstalls all hooks.
    /// </summary>
    /// <exception cref="HookUninstallFailedException">
    ///   Thrown if the certain hook fails to installs.
    /// </exception>
    public void UninstallAll()
    {
      foreach (var hook in this.Where(hook => !hook.Value.Uninstall()))
      {
        throw new HookUninstallFailedException(
          "The hook " + hook.Key + " failed to uninstall. Verify addresses are correct.");
      }
    }

    #endregion

    ~InsiderManager()
    {
      UninstallAll();
    }
  }
}
