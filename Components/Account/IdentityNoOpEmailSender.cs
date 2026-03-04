using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.UI.Services;
using ARIS1.Data;
using ARIS1.Models;

namespace ARIS1.Components.Account;

internal sealed class NoOpEmailSender : IEmailSender<User>
{
    public Task SendConfirmationLinkAsync(User user, string email, string confirmationLink) => Task.CompletedTask;
    public Task SendPasswordResetLinkAsync(User user, string email, string resetLink) => Task.CompletedTask;
    public Task SendPasswordResetCodeAsync(User user, string email, string resetCode) => Task.CompletedTask;
}