using System;

namespace Insider
{
  /// <summary>
  ///   Thrown if a hook cannot be installed.
  /// </summary>
  public class HookInstallFailedException : SystemException
  {
    #region Constructors and Destructors

    /// <summary>
    ///   Initializes a new instance of the <see cref="HookInstallFailedException" /> class.
    /// </summary>
    /// <param name="errorMessage">
    ///   The error message.
    /// </param>
    public HookInstallFailedException(string errorMessage)
      : base(errorMessage)
    {
    }

    #endregion
  }
}
