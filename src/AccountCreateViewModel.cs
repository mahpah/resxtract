using AutoMapper;
using Dgm.Core;
using Dgm.Core.Enums;
using Dgm.Core.Errors;
using EAudit.Common;
using EAudit.Common.Constants;
using EAudit.Model.Entities;
using EAudit.Service.Dto;
using EAudit.Service.Interfaces;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;

namespace EAudit.Service
{

    public class AccountService : IAccountService
    {
        private readonly IConfigurationRoot _configuration;
        private readonly UserManager<Account> _userManager;
        private readonly RoleManager<IdentityRole> _roleManager;
        private readonly PasswordHasher<Account> _passwordHasher;
        private readonly IMapper _mapper;
        private readonly IEmailService _emailService;

        public AccountService(IConfigurationRoot configuration, UserManager<Account> userManager, IMapper mapper, IEmailService emailService, RoleManager<IdentityRole> roleManager)
        {
            _configuration = configuration;
            _userManager = userManager;
            _passwordHasher = new PasswordHasher<Account>();
            _mapper = mapper;
            _emailService = emailService;
            _roleManager = roleManager;
        }

        public async Task<QueryResultDto<AccountDto>> Query(int skip, int take, string sortByField, SortDirection sortDirection, string searchQuery, params Expression<Func<Account, bool>>[] extraPredicates)
        {
            IOrderedQueryable<Account> queryableEntities = null;
            var queryExp = string.IsNullOrWhiteSpace(searchQuery) ? (t => true) : GetSearchExpression(searchQuery.ToLower());
            if (extraPredicates != null && extraPredicates.Any())
            {
                queryExp = extraPredicates.Aggregate(queryExp, (memo, pred) => memo.And(pred));
            }
            var queryable = _userManager.Users.Include(t => t.Roles).Where(queryExp);
            var sortExpressions = GetSortBy(sortByField).ToList();
            for (var i = 0; i < sortExpressions.Count; i++)
            {
                var sortExp = sortExpressions[i];
                if (sortDirection == SortDirection.Descending)
                {
                    queryableEntities = i == 0 ? queryable.OrderByDescending(sortExp) : queryableEntities.ThenByDescending(sortExp);
                }
                else
                {
                    queryableEntities = i == 0 ? queryable.OrderBy(sortExp) : queryableEntities.ThenBy(sortExp);
                }
            }
            var items = queryableEntities
                .Skip(skip)
                .Take(take)
                .ToList()
                .Select(account =>
                {
                    var dto = _mapper.Map<AccountDto>(account);
                    dto.Roles = _userManager.GetRolesAsync(account).GetAwaiter().GetResult();
                    return dto;
                });
            return new QueryResultDto<AccountDto>
            {
                Count = await _userManager.Users.CountAsync(queryExp),
                Items = items
            };
        }

        public async Task<AccountDto> Get(string id)
        {
            var account = await _userManager.Users
                .Include(user => user.ProfileImage)
                .FirstOrDefaultAsync(u => u.Id == id);
            if (account == null)
            {
                throw new DgmException("No users found with this Id");
            }

            var roles = await _userManager.GetRolesAsync(account);
            var dto = _mapper.Map<AccountDto>(account);
            dto.Roles = roles;
            return dto;
        }

        public async Task Delete(string id)
        {
            var user = await _userManager.FindByIdAsync(id);
            if (user == null)
            {
                throw new DgmException("No users found with this Id");
            }

            await PreventNoAdmin(user);
            var result = await _userManager.DeleteAsync(user);
            result.EnsureSucceed();
        }

        public async Task<AccountDto> Create(AccountDto accountDto, bool isNotifyEmail)
        {
            var account = _mapper.Map<Account>(accountDto);
            account.CreatedDate = DateTime.Now;
            account.UpdatedDate = account.CreatedDate;
            var result = await _userManager.CreateAsync(account, accountDto.Password);
            result.EnsureSucceed();
            result = await _userManager.AddToRolesAsync(account, accountDto.Roles);
            result.EnsureSucceed();

            if (isNotifyEmail)
            {
                _emailService.SendEmail(new EmailMessageDto
                {
                    EmailTemplateKey = EmailTemplateKeys.NotificationWelcomeToNewAccount,
                    Data = accountDto,
                    To = accountDto.Email,
                });
            }

            return await Get(account.Id);
        }

        public async Task<AccountDto> CreateWithoutPassword(AccountDto accountDto)
        {
            var account = _mapper.Map<Account>(accountDto);
            account.CreatedDate = DateTime.Now;
            account.UpdatedDate = account.CreatedDate;
            var result = await _userManager.CreateAsync(account);
            result.EnsureSucceed();
            result = await _userManager.AddToRolesAsync(account, accountDto.Roles);
            result.EnsureSucceed();
            var passwordToken = await _userManager.GeneratePasswordResetTokenAsync(account);

            var url = _configuration.GetValue<string>(ConfigurationKeys.SetAccountPasswordUrl)
                .Replace("[Token]", passwordToken)
                .Replace("[EmailAddress]", account.Email);
            url = _configuration.GetValue<string>(ConfigurationKeys.PortalUrl) + url;

            _emailService.SendEmail(new EmailMessageDto
            {
                EmailTemplateKey = EmailTemplateKeys.RequestSetPasswordForNewAccount,
                Data = new
                {
                    Url = url,
                    FirstName = account.FirstName,
                    LastName = account.LastName,
                },
                To = accountDto.Email
            });

            return await Get(account.Id);
        }

        public async Task<AccountDto> UpdateOwn(string accountId, AccountOwnUpdateDto accountOwnUpdateDto)
        {
            // prevent same key exception
            var account = await _userManager.FindByIdAsync(accountId);
            _mapper.Map(accountOwnUpdateDto, account); // bind value like a boss
            account.UpdatedDate = DateTime.Now;
            var result = await _userManager.UpdateAsync(account);
            result.EnsureSucceed();

            return await Get(account.Id);
        }

        public async Task<AccountDto> Update(AccountUpdateDto accountUpdateDto)
        {
            // prevent same key exception
            var account = await _userManager.FindByIdAsync(accountUpdateDto.Id);

            var newRoles = accountUpdateDto.Roles.ToList();
            var oldRoles = await _userManager.GetRolesAsync(account);
            var rolesToRemove = oldRoles.Except(newRoles);
            var rolesToAdd = newRoles.Except(oldRoles);

            _mapper.Map(accountUpdateDto, account); // bind value like a boss
            account.UpdatedDate = DateTime.Now;
            var result = await _userManager.UpdateAsync(account);
            result.EnsureSucceed();

            result = await _userManager.RemoveFromRolesAsync(account, rolesToRemove);
            result.EnsureSucceed();

            result = await _userManager.AddToRolesAsync(account, rolesToAdd);
            result.EnsureSucceed();

            return await Get(account.Id);
        }

        public async Task UpdateEmail(string accountId, string emailAddress)
        {
            var account = await _userManager.FindByIdAsync(accountId);
            var result = await _userManager.SetUserNameAsync(account, emailAddress);
            result.EnsureSucceed();
            result = await _userManager.SetEmailAsync(account, emailAddress);
            result.EnsureSucceed();
        }

        public async Task UpdatePassword(string accountId, string newPassword)
        {
            var account = await _userManager.FindByIdAsync(accountId);
            account.PasswordHash = _passwordHasher.HashPassword(account, newPassword);
            var result = await _userManager.UpdateAsync(account);
            result.EnsureSucceed();
        }

        public async Task UpdateOwnPassword(string accountId, string currentPassword, string newPassword)
        {
            var account = await _userManager.FindByIdAsync(accountId);
            var result = await _userManager.ChangePasswordAsync(account, currentPassword, newPassword);
            result.EnsureSucceed();
        }

        public async Task ActivateAccount(string accountId, bool isNotifyEmail)
        {
            var account = await _userManager.FindByIdAsync(accountId);
            account.IsActive = true;
            account.UpdatedDate = DateTime.Now;
            var result = await _userManager.UpdateAsync(account);
            result.EnsureSucceed();

            if (!isNotifyEmail)
            {
                return;
            }

            var activeAccountDto = _mapper.Map<AccountDto>(account);
            _emailService.SendEmail(new EmailMessageDto
            {
                EmailTemplateKey = EmailTemplateKeys.NotificationActivateAccount,
                Data = activeAccountDto,
                To = activeAccountDto.Email,
            });
        }

        public async Task DeactivateAccount(string accountId, bool isNotifyEmail)
        {
            var account = await _userManager.FindByIdAsync(accountId);
            account.IsActive = false;
            account.UpdatedDate = DateTime.Now;
            var result = await _userManager.UpdateAsync(account);
            result.EnsureSucceed();

            if (!isNotifyEmail)
            {
                return;
            }

            var deactiveAccountDto = _mapper.Map<AccountDto>(account);
            _emailService.SendEmail(new EmailMessageDto
            {
                EmailTemplateKey = EmailTemplateKeys.NotificationDeactiveAccount,
                Data = deactiveAccountDto,
                To = deactiveAccountDto.Email,
            });
        }

        public async Task ForgotPassword(string emailAddress)
        {
            var account = await _userManager.FindByNameAsync(emailAddress);
            if (account == null)
            {
                throw new DgmException(nameof(emailAddress), "Email doesn't link to the existing account.");
            }

            var passwordToken = await _userManager.GeneratePasswordResetTokenAsync(account);
            var activeAccountDto = _mapper.Map<AccountDto>(account);
            var resetPasswordLink = _configuration[ConfigurationKeys.ResetPasswordUrl]
                ?.Replace("[Token]", passwordToken)
                ?.Replace("[EmailAddress]", emailAddress);
            resetPasswordLink = _configuration.GetValue<string>(ConfigurationKeys.PortalUrl) + resetPasswordLink;

            _emailService.SendEmail(new EmailMessageDto
            {
                EmailTemplateKey = EmailTemplateKeys.ForgotPassword,
                Data = new
                {
                    Token = passwordToken,
                    FullName = $"{activeAccountDto.FirstName} {activeAccountDto.LastName}",
                    Email = activeAccountDto.Email,
                    UserName = activeAccountDto.Email,
                    Url = resetPasswordLink
                },
                To = activeAccountDto.Email,
            });
        }

        public async Task ResetPassword(string emailAddress, string passwordToken, string newPassword)
        {
            var account = await _userManager.FindByNameAsync(emailAddress);
            if (account == null)
            {
                throw new DgmException(nameof(emailAddress), "Email doesn't link to the existing account.");
            }

            var result = await _userManager.ResetPasswordAsync(account, passwordToken, newPassword);
            result.EnsureSucceed();
        }

        public async Task<bool> VerifyPasswordToken(string emailAddress, string passwordToken)
        {
            var account = await _userManager.FindByNameAsync(emailAddress);
            if (account == null)
            {
                throw new DgmException(nameof(emailAddress), "Email doesn't link to the existing account.");
            }
            var isValidPasswordToken = await _userManager.VerifyUserTokenAsync(account, TokenOptions.DefaultProvider, "ResetPassword", passwordToken);
            return isValidPasswordToken;
        }

        public async Task<QueryResultDto<AccountDto>> FilterByRole(string roleName, int skip, int take, string sortByField, SortDirection sortDirection, string searchQuery)
        {
            var roleIds = _roleManager.Roles.Where(r => r.NormalizedName == (roleName ?? "").ToUpper()).Select(t => t.Id);
            if (!roleIds.Any())
            {
                throw new DgmException(nameof(abc), _localizer["Role is empty or invalid"]);
            }
            return await Query(skip, take, sortByField, sortDirection, searchQuery, t => t.Roles.Any(r => roleIds.Contains(r.RoleId)));
        }

        public async Task<IList<AccountDto>> EnsureAccountForTestTakers(IEnumerable<string> emailAddresses)
        {
            return await Task.WhenAll(emailAddresses.Select(async x => await EnsureAccountForSingleTeskTaker(x)).ToList());
        }

        private async Task<AccountDto> EnsureAccountForSingleTeskTaker(string emailAddress)
        {
            var user = await _userManager.FindByNameAsync(emailAddress);
            if (user == null)
            {
                var newAccount = new Account
                {
                    UserName = emailAddress,
                    Email = emailAddress,
                    CreatedDate = DateTime.Now,
                    UpdatedDate = DateTime.Now,
                    IsActive = true
                };
                await _userManager.CreateAsync(newAccount);
                await _userManager.AddToRoleAsync(newAccount, Roles.TestTaker);
                var newAccountDto = _mapper.Map<AccountDto>(newAccount);
                newAccountDto.PasswordResetToken = await _userManager.GeneratePasswordResetTokenAsync(newAccount);
                return newAccountDto;
            }

            if (!await _userManager.IsInRoleAsync(user, Roles.TestTaker))
            {
                await _userManager.AddToRoleAsync(user, Roles.TestTaker);
            }
            return _mapper.Map<AccountDto>(user);
        }

        private static IEnumerable<Expression<Func<Account, object>>> GetSortBy(string sortByField)
        {
            switch (sortByField)
            {
                case "updatedDate":
                    return new List<Expression<Func<Account, object>>> { t => t.UpdatedDate };
                case "createdDate":
                    return new List<Expression<Func<Account, object>>> { t => t.CreatedDate };
                case "lastName":
                    return new List<Expression<Func<Account, object>>> { t => t.LastName };
                case "firstName":
                    return new List<Expression<Func<Account, object>>> { t => t.FirstName };
                case "email":
                case "userName":
                    return new List<Expression<Func<Account, object>>> { t => t.Email };
                case "name":
                    return new List<Expression<Func<Account, object>>> { t => t.LastName, t => t.FirstName };
                default:
                    return new List<Expression<Func<Account, object>>> { t => t.CreatedDate };
            }
        }

        [Test(ErrorMessage="adsfasf")]
        private async Task PreventNoAdmin(Account user)
        {
            var roles = await _userManager.GetRolesAsync(user);
            if (!roles.Contains(Roles.Admin))
            {
                return;
            }
            var adminUsers = await _userManager.GetUsersInRoleAsync(Roles.Admin);
            if (adminUsers.Count <= 1)
            {
                throw new DgmException("Id", "Cannot delete or deactivate the last user with Admin role in the system");
            }
        }

        private static Expression<Func<Account, bool>> GetSearchExpression(string searchQuery)
        {
            Expression<Func<Account, bool>> exp = t => true;
            var terms = Regex.Split(searchQuery, @"\s+");
            foreach (var t in terms)
            {
                if (exp == null)
                {
                    exp = c => (c.FirstName.ToLower() + " " + c.LastName.ToLower() + " " + c.Email.ToLower()).Contains(t);
                }
                else
                {
                    exp = exp.And(c => (c.FirstName.ToLower() + " " + c.LastName.ToLower() + " " + c.Email.ToLower()).Contains(t));
                }
            }
            return exp;
        }
    }
}
