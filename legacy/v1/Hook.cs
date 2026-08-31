using System;
using System.Reflection;
using System.Runtime.InteropServices;

namespace Insider
{
  /// <summary>
  ///   Handles managed API hooking.
  ///   !!!Don't use with unmanaged API!!!
  /// </summary>
  public class Hook
  {
    #region Public Properties

    /// <summary>
    ///   Gets a value indicating whether the hook is installed.
    /// </summary>
    public bool IsInstalled { get; private set; }

    #endregion

    //private Int32 Diff32 = 0x10;
    //private Int32 Diff64 = 0x18;

    /* Only for test purposes
    private void DumpMemory(string message)
    {
      var dump = new byte[1030];
      var start = new IntPtr(target.ToInt64() - 1024L);
      Marshal.Copy(start, dump, 0, 1030);
      var s = string.Empty;
      foreach (var b in dump)
      {
        s += string.Format("{0:X2} ", b);
      }
      var indexE8 = 0;
      for (var i = dump.Length - 1; i > 0; i--)
      {
        if (dump[i] == 232)
          break;
        indexE8++;
      }
    }
    */

    private enum PlatformTarget
    {
      Microsoft = 0,
      Mono = 1
    }

    #region Public Methods and Operators

    /// <summary>
    ///   Calls the original function, and returns a return value.
    /// </summary>
    /// <param name="args">
    ///   The arguments to pass. If it is a 'void' argument list,
    ///   you must pass 'null'.
    /// </param>
    /// <returns>
    ///   An object containing the original functions return value.
    /// </returns>
    public object CallOriginal(params object[] args)
    {
      Uninstall();
      var ret = _targetDelegate.DynamicInvoke(args);
      Install();
      return ret;
    }

    /// <summary>
    ///   Installs the hook.
    /// </summary>
    /// <returns>
    ///   Whether the operation was successful.
    /// </returns>
    public bool Install()
    {
      try
      {
        if (_mPlatformTarget == PlatformTarget.Microsoft)
        {
          Marshal.Copy(_newBytes, 0, _target, _newBytes.Length);
        }
        else
        {
          unsafe
          {
            var sitePtr = (byte*) _target.ToPointer();
            *sitePtr = 0x49; // mov r11, target
            *(sitePtr + 1) = 0xBB;
            *((ulong*) (sitePtr + 2)) = (ulong) _hook.ToInt64();
            *(sitePtr + 10) = 0x41; // jmp r11
            *(sitePtr + 11) = 0xFF;
            *(sitePtr + 12) = 0xE3;
          }
        }
        IsInstalled = true;
        return true;
      }
      catch (Exception)
      {
        IsInstalled = false;
        return false;
      }
    }

    /// <summary>
    ///   Removes the hook.
    /// </summary>
    /// <returns>
    ///   Whether the operation was successful.
    /// </returns>
    public bool Uninstall()
    {
      try
      {
        Marshal.Copy(_originalBytes, 0, _target, _originalBytes.Length);
        IsInstalled = false;
        return true;
      }
      catch (Exception)
      {
        IsInstalled = true;
        return false;
      }
    }

    #endregion

    #region Constants and Fields

    /// <summary>
    ///   The pointer to the hook.
    /// </summary>
    private readonly IntPtr _hook;

    /// <summary>
    ///   The method where to redirect your target
    /// </summary>
    private readonly MethodInfo _mHook;

    /// <summary>
    ///   Enum for internal operation
    /// </summary>
    private readonly PlatformTarget _mPlatformTarget;

    /// <summary>
    ///   The method to hook
    /// </summary>
    private readonly MethodInfo _mTarget;

    /// <summary>
    ///   The new bytes to be written to the target.
    /// </summary>
    private readonly byte[] _newBytes;

    /// <summary>
    ///   The original  bytes read from the target.
    /// </summary>
    private readonly byte[] _originalBytes;

    /// <summary>
    ///   The pointer to the target.
    /// </summary>
    private readonly IntPtr _target;

    /// <summary>
    ///   The delegate method of the target.
    /// </summary>
    private readonly Delegate _targetDelegate;

    #endregion

    #region Constructors and Destructors

    /// <summary>
    ///   Initializes a new instance of the <see cref="Hook" /> class.
    /// </summary>
    /// <param name="target">
    ///   The target.
    /// </param>
    /// <param name="hook">
    ///   The hook.
    /// </param>
    public Hook(Delegate target, Delegate hook)
    {
      _mPlatformTarget = PlatformTarget.Microsoft;
      _targetDelegate = target;

      _hook = hook.Method.MethodHandle.GetFunctionPointer();
      _target = target.Method.MethodHandle.GetFunctionPointer();

      byte[] hookPointerBytes;

      if (IntPtr.Size == 8)
      {
        _originalBytes = new byte[6];
        Marshal.Copy(_target, _originalBytes, 0, 6);
        var diff = _hook.ToInt64() - _target.ToInt64() - 5L;
        hookPointerBytes = BitConverter.GetBytes(Convert.ToInt32(diff));
        _newBytes = new byte[]
        {
          0xE9, hookPointerBytes[0], hookPointerBytes[1], hookPointerBytes[2], hookPointerBytes[3]
        };
      }
      else
      {
        _originalBytes = new byte[6];
        Marshal.Copy(_target, _originalBytes, 0, 6);
        hookPointerBytes = BitConverter.GetBytes(_hook.ToInt32());
        _newBytes = new byte[]
        {
          0x68, hookPointerBytes[0], hookPointerBytes[1], hookPointerBytes[2], hookPointerBytes[3], 0xC3
        };
      }
    }

    /// <summary>
    ///   Initializes a new instance of the <see cref="Hook" /> class.
    /// </summary>
    /// <param name="target">
    ///   The target method.
    /// </param>
    /// <param name="hook">
    ///   The hook method.
    /// </param>
    /// <param name="targetDelegate">
    ///   The original delegate built up from the target method
    /// </param>
    public Hook(MethodInfo target, MethodInfo hook, Delegate targetDelegate)
    {
      _mPlatformTarget = PlatformTarget.Mono;
      _targetDelegate = targetDelegate;
      _mTarget = target;
      _mHook = hook;

      _target = _mTarget.MethodHandle.GetFunctionPointer();
      _hook = _mHook.MethodHandle.GetFunctionPointer();

      _originalBytes = new byte[13];
      Marshal.Copy(_target, _originalBytes, 0, 13);
    }

    #endregion
  }
}
