using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using AbpIdentityUser = Volo.Abp.Identity.IdentityUser;

namespace CarDealership.CoreService.Identity;

public  class PasswordLengthValidator : IPasswordValidator<AbpIdentityUser>
{
   public const string ErrorCode = "PasswordLengthOutOfRange";

   public Task<IdentityResult> ValidateAsync(UserManager<AbpIdentityUser> manager, AbpIdentityUser user, string? password)
   {
      var length = password?.Length ?? 0;

      if (length < PasswordPolicy.MinLength || length > PasswordPolicy.MaxLength)
      {
         return Task.FromResult(IdentityResult.Failed(new IdentityError
         {
            Code = ErrorCode,
            Description =
               $"Password must be between {PasswordPolicy.MinLength} and {PasswordPolicy.MaxLength} characters."
         }));
      }

      return Task.FromResult(IdentityResult.Success);
   }
}