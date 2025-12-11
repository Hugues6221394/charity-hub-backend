using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace StudentCharityHub.Extensions
{
    public static class ModelStateExtensions
    {
        public static void AddAllErrors(this ModelStateDictionary modelState, IDictionary<string, string> errors)
        {
            foreach (var kvp in errors)
            {
                modelState.AddModelError(kvp.Key, kvp.Value);
            }
        }
    }
}


