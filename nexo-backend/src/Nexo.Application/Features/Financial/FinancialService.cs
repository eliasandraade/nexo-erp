using Nexo.Application.Common.Interfaces;
using Nexo.Domain.Entities;
using Nexo.Domain.Enums;
using Nexo.Domain.Exceptions;

namespace Nexo.Application.Features.Financial;

public class FinancialService
{
    private readonly IFinancialRepository _financial;
    private readonly ICurrentTenant _currentTenant;
    private readonly ICurrentUser _currentUser;

    public FinancialService(IFinancialRepository financial, ICurrentTenant currentTenant, ICurrentUser currentUser)
    {
        _financial     = financial;
        _currentTenant = currentTenant;
        _currentUser   = currentUser;
    }

    // ── Accounts ─────────────────────────────────────────────────────────────

    public async Task<IReadOnlyList<FinancialAccountDto>> GetAllAccountsAsync(bool includeInactive = false, CancellationToken ct = default)
    {
        var list = await _financial.GetAllAccountsAsync(includeInactive, ct);
        return list.Select(MapAccountToDto).ToList();
    }

    public async Task<FinancialAccountDto> GetAccountByIdAsync(Guid id, CancellationToken ct = default)
    {
        var account = await _financial.GetAccountByIdAsync(id, ct)
            ?? throw new NotFoundException("FinancialAccount", id);
        return MapAccountToDto(account);
    }

    public async Task<FinancialAccountDto> CreateAccountAsync(CreateFinancialAccountRequest request, CancellationToken ct = default)
    {
        if (await _financial.AccountCodeExistsAsync(request.Code, ct: ct))
            throw new ConflictException($"Account code '{request.Code}' is already in use.");

        var accountType = Enum.Parse<FinancialAccountType>(request.AccountType, ignoreCase: true);

        var account = FinancialAccount.Create(
            _currentTenant.Id,
            request.Code,
            request.Name,
            accountType,
            request.ParentAccountId);

        await _financial.AddAccountAsync(account, ct);
        await _financial.SaveChangesAsync(ct);
        return MapAccountToDto(account);
    }

    public async Task<FinancialAccountDto> UpdateAccountAsync(Guid id, UpdateFinancialAccountRequest request, CancellationToken ct = default)
    {
        var account = await _financial.GetAccountByIdAsync(id, ct)
            ?? throw new NotFoundException("FinancialAccount", id);

        if (await _financial.AccountCodeExistsAsync(request.Code, excludeId: id, ct: ct))
            throw new ConflictException($"Account code '{request.Code}' is already in use.");

        account.Update(request.Code, request.Name, request.ParentAccountId);
        await _financial.SaveChangesAsync(ct);
        return MapAccountToDto(account);
    }

    public async Task DeactivateAccountAsync(Guid id, CancellationToken ct = default)
    {
        var account = await _financial.GetAccountByIdAsync(id, ct)
            ?? throw new NotFoundException("FinancialAccount", id);
        account.Deactivate();
        await _financial.SaveChangesAsync(ct);
    }

    public async Task ActivateAccountAsync(Guid id, CancellationToken ct = default)
    {
        var account = await _financial.GetAccountByIdAsync(id, ct)
            ?? throw new NotFoundException("FinancialAccount", id);
        account.Activate();
        await _financial.SaveChangesAsync(ct);
    }

    // ── Transactions ─────────────────────────────────────────────────────────

    public async Task<IReadOnlyList<FinancialTransactionDto>> GetTransactionsByAccountAsync(Guid accountId, CancellationToken ct = default)
    {
        _ = await _financial.GetAccountByIdAsync(accountId, ct)
            ?? throw new NotFoundException("FinancialAccount", accountId);

        var list = await _financial.GetTransactionsByAccountAsync(accountId, ct);
        return list.Select(MapTransactionToDto).ToList();
    }

    public async Task<IReadOnlyList<FinancialTransactionDto>> GetPendingTransactionsAsync(CancellationToken ct = default)
    {
        var list = await _financial.GetPendingTransactionsAsync(ct);
        return list.Select(MapTransactionToDto).ToList();
    }

    public async Task<FinancialTransactionDto> GetTransactionByIdAsync(Guid id, CancellationToken ct = default)
    {
        var tx = await _financial.GetTransactionByIdAsync(id, ct)
            ?? throw new NotFoundException("FinancialTransaction", id);
        return MapTransactionToDto(tx);
    }

    public async Task<FinancialTransactionDto> CreateTransactionAsync(CreateTransactionRequest request, CancellationToken ct = default)
    {
        _ = await _financial.GetAccountByIdAsync(request.FinancialAccountId, ct)
            ?? throw new NotFoundException("FinancialAccount", request.FinancialAccountId);

        var transactionType = Enum.Parse<TransactionType>(request.TransactionType, ignoreCase: true);

        var tx = FinancialTransaction.Create(
            _currentTenant.Id,
            request.FinancialAccountId,
            transactionType,
            request.Amount,
            request.Description,
            request.DueDate,
            _currentUser.UserId,
            request.ReferenceType,
            request.ReferenceId);

        await _financial.AddTransactionAsync(tx, ct);
        await _financial.SaveChangesAsync(ct);
        return MapTransactionToDto(tx);
    }

    public async Task<FinancialTransactionDto> UpdateTransactionAsync(Guid id, UpdateTransactionRequest request, CancellationToken ct = default)
    {
        var tx = await _financial.GetTransactionByIdAsync(id, ct)
            ?? throw new NotFoundException("FinancialTransaction", id);

        if (tx.Status != TransactionStatus.Pending)
            throw new DomainException("Only pending transactions can be updated.");

        tx.Update(request.Amount, request.Description, request.DueDate);
        await _financial.SaveChangesAsync(ct);
        return MapTransactionToDto(tx);
    }

    public async Task<FinancialTransactionDto> MarkPaidAsync(Guid id, MarkTransactionPaidRequest request, CancellationToken ct = default)
    {
        var tx = await _financial.GetTransactionByIdAsync(id, ct)
            ?? throw new NotFoundException("FinancialTransaction", id);

        tx.MarkPaid(request.PaidAt);
        await _financial.SaveChangesAsync(ct);
        return MapTransactionToDto(tx);
    }

    public async Task<FinancialTransactionDto> CancelTransactionAsync(Guid id, CancellationToken ct = default)
    {
        var tx = await _financial.GetTransactionByIdAsync(id, ct)
            ?? throw new NotFoundException("FinancialTransaction", id);

        if (tx.Status == TransactionStatus.Cancelled)
            throw new DomainException("Transaction is already cancelled.");

        tx.Cancel();
        await _financial.SaveChangesAsync(ct);
        return MapTransactionToDto(tx);
    }

    private static FinancialAccountDto MapAccountToDto(FinancialAccount a) => new(
        a.Id, a.Code, a.Name, a.AccountType.ToString(), a.ParentAccountId, a.IsActive, a.CreatedAt, a.UpdatedAt);

    private static FinancialTransactionDto MapTransactionToDto(FinancialTransaction t) => new(
        t.Id,
        t.FinancialAccountId,
        t.FinancialAccount?.Name ?? string.Empty,
        t.TransactionType.ToString(),
        t.Amount,
        t.Description,
        t.DueDate,
        t.PaidAt,
        t.Status.ToString(),
        t.ReferenceType,
        t.ReferenceId,
        t.CreatedByUserId,
        t.CreatedAt,
        t.UpdatedAt);
}
