using System;

namespace Insider
{
  /// <summary>
  ///   Thrown if a hook cannot be found.
  /// </summary>
  public class HookNotFoundException : SystemException
  {
    #region Constructors and Destructors

    /// <summary>
    ///   Initializes a new instance of the <see cref="HookNotFoundException" /> class.
    /// </summary>
    /// <param name="errorMessage">
    ///   The error message.
    /// </param>
    public HookNotFoundException(string errorMessage)
      : base(errorMessage)
    {
    }

    #endregion
  }
}
