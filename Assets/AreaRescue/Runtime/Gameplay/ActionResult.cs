using UnityEngine;

namespace DemonWeave.AreaRescue
{
    public readonly struct ActionResult
    {
        public bool Success { get; }
        public string Message { get; }
        public ActionResult(bool success, string message) { Success = success; Message = message; }
    }

}

