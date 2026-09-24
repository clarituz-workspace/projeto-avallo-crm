using Avallo.Client.Models;

namespace Avallo.Client.Services;

public sealed class FiscalService(AuthService authService)
{
    public Task<ApiResult<FiscalOverviewModel>> GetOverviewAsync(CancellationToken cancellationToken = default) =>
        authService.GetAsync<FiscalOverviewModel>("api/fiscal/overview", cancellationToken);
    public Task<ApiResult<CnpjLookupModel>> LookupCnpjAsync(string cnpj, CancellationToken cancellationToken = default) =>
        authService.GetAsync<CnpjLookupModel>(
            $"api/fiscal/cnpj/{new string(cnpj.Where(char.IsDigit).ToArray())}", cancellationToken);
    public Task<ApiResult<TaxProfileModel>> CreateProfileAsync(
        string cnpj, string taxRegime, DateOnly effectiveFrom, CancellationToken cancellationToken = default) =>
        authService.PostAsync<object, TaxProfileModel>("api/fiscal/profiles",
            new { cnpj, taxRegime, effectiveFrom }, cancellationToken);
    public Task<ApiResult<TaxRuleModel>> CreateRuleAsync(
        Guid profileId, string taxCode, string taxName, decimal rate, DateOnly effectiveFrom,
        CancellationToken cancellationToken = default) =>
        authService.PostAsync<object, TaxRuleModel>("api/fiscal/rules",
            new { taxProfileId = profileId, taxCode, taxName, rate, effectiveFrom }, cancellationToken);
    public Task<ApiResult<TaxRuleModel>> SubmitRuleAsync(Guid id, CancellationToken cancellationToken = default) =>
        authService.PostAsync<TaxRuleModel>($"api/fiscal/rules/{id}/submit", cancellationToken);
    public Task<ApiResult<TaxRuleModel>> ApproveRuleAsync(Guid id, CancellationToken cancellationToken = default) =>
        authService.PostAsync<TaxRuleModel>($"api/fiscal/rules/{id}/approve", cancellationToken);
    public Task<ApiResult<TaxRuleModel>> RejectRuleAsync(Guid id, string notes, CancellationToken cancellationToken = default) =>
        authService.PostAsync<object, TaxRuleModel>($"api/fiscal/rules/{id}/reject", new { notes }, cancellationToken);
    public Task<ApiResult<TaxSimulationModel>> SimulateAsync(
        decimal amount, DateOnly date, CancellationToken cancellationToken = default) =>
        authService.PostAsync<object, TaxSimulationModel>("api/fiscal/simulate", new { amount, date }, cancellationToken);
    public Task<ApiResult<ReprocessTaxModel>> ReprocessAsync(CancellationToken cancellationToken = default) =>
        authService.PostAsync<ReprocessTaxModel>("api/fiscal/reprocess", cancellationToken);
}
