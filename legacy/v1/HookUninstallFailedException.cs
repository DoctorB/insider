using System;

namespace Insider
{
  /// <summary>
  ///   Thrown if a hook cannot be removed.
  /// </summary>
  public class HookUninstallFailedException : SystemException
  {
    #region Constructors and Destructors

    /// <summary>
    ///   Initializes a new instance of the <see cref="HookUninstallFailedException" /> class.
    /// </summary>
    /// <param name="errorMessage">
    ///   The error message.
    /// </param>
    public HookUninstallFailedException(string errorMessage)
      : base(errorMessage)
    {
    }

    #endregion
  }
}
