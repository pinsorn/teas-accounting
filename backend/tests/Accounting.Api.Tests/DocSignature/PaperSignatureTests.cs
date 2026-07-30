using Accounting.Api.Tests.Fixtures;
using Accounting.Application.Abstractions;
using Accounting.Application.Master;
using Accounting.Application.Purchase;
using Accounting.Application.Sales;
using Accounting.Domain.Entities.Identity;
using Accounting.Domain.Entities.Master;
using Accounting.Domain.Entities.Sys;
using Accounting.Domain.Entities.Tax;
using Accounting.Domain.Enums;
using Accounting.Infrastructure;
using Accounting.Infrastructure.Persistence;
using Accounting.TestKit;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using UglyToad.PdfPig;
using Xunit;

namespace Accounting.Api.Tests.DocSignature;

/// <summary>
/// doc-signature-and-foot-layout spec — WP-3 (renderer + resolution). Covers T1-T9 (+T6b) + T17
/// (§9). **T10 lives in `Pdf/PaperEndpointTests.cs`** (it extends that file's existing
/// seller.logo/logoSvg JsonIgnore pattern against the real /paper HTTP endpoint — the
/// Tier-2 round confirmed the original claim of "T1-T10" here was false; T10 was never
/// implemented until the corresponding remediation). Assert on the PaperDocModel DTO for
/// resolution/gating (fast, deterministic); reserve PDF/PdfPig parsing for pagination (T6-T9)
/// and the ASCII name-line assertions (T17), per §1.9.4 — never assert on Thai text extracted
/// from a PDF (tone marks drop on extraction, and the drop is itself non-deterministic —
/// see `NormalizeForComparison` below and the troubles-wiki.md entry it links).
/// </summary>
[Collection(nameof(PostgresCollection))]
public sealed class PaperSignatureTests : IDisposable
{
    private readonly PostgresFixture _fx;
    private readonly string _root =
        Path.Combine(Path.GetTempPath(), "teas-it-" + Guid.NewGuid().ToString("N")[..8]);

    public PaperSignatureTests(PostgresFixture fx) => _fx = fx;
    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    // Program.cs sets the QuestPDF Community license at API startup; these tests exercise
    // the builder directly (no host), so set it once here too (mirrors PurchasePdfTests).
    static PaperSignatureTests() =>
        QuestPDF.Settings.License = QuestPDF.Infrastructure.LicenseType.Community;

    private static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow.AddHours(7));

    private ServiceProvider Provider(int companyId, int branchId, long userId, string? storageRoot = null)
    {
        var dict = new Dictionary<string, string?> { ["ConnectionStrings:Postgres"] = _fx.ConnectionString };
        if (storageRoot is not null) dict["FileStorage:StorageRoot"] = storageRoot;
        var cfg = new ConfigurationBuilder().AddInMemoryCollection(dict).Build();
        var s = new ServiceCollection();
        s.AddLogging();
        return s.AddInfrastructure(cfg)
            .AddSingleton<ITenantContext>(new StubTenant
            { CompanyId = companyId, BranchId = branchId, UserId = userId, IsSuperAdmin = false })
            .BuildServiceProvider();
    }

    /// <summary>A real sys.users row with a controllable FullName/Position (no role needed —
    /// PaperSignatureSource resolves by user id alone, membership is only enforced at upload).
    /// Assigns a role so UploadSignatureAsync's membership guard (AttachmentService
    /// .ParentExistsAsync's UserSignature arm, §E3) passes for this actor.</summary>
    private async Task<long> CreateActorUserAsync(int companyId, string fullName, string? position = null)
    {
        await using var sp = Provider(companyId, 1, userId: 1);
        await using var scope = sp.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AccountingDbContext>();
        await db.Database.ExecuteSqlRawAsync(
            "SELECT setval(pg_get_serial_sequence('sys.users','user_id'), " +
            "(SELECT COALESCE(MAX(user_id),0)+1 FROM sys.users), false);");
        var user = new User
        {
            Username = "u-" + TestIds.Suffix(), Email = TestIds.Email(), PasswordHash = "x",
            FullName = fullName, Position = position, IsActive = true,
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var roleId = await db.Roles.Where(r => r.CompanyId == companyId && r.RoleCode == Role.SystemRoles.Accountant)
            .Select(r => r.RoleId).FirstAsync();
        db.UserRoles.Add(new UserRole
        {
            UserId = user.UserId, RoleId = roleId, CompanyId = companyId,
            BranchId = 0, ValidFrom = DateOnly.FromDateTime(DateTime.UtcNow), ValidTo = null,
        });
        await db.SaveChangesAsync();
        return user.UserId;
    }

    /// <summary>Uploads a real signature/stamp image via the real service (real file on
    /// disk under _root) — the storage-footgun-safe pattern (mirrors Sprint11AttachmentTests).</summary>
    // A real, minimal 1x1 PNG (not just 4 arbitrary bytes) — some tests (T3/T17) actually
    // render the PDF, and QuestPDF's SkiaSharp image decoder rejects non-image bytes outright.
    private static readonly byte[] MinimalPng = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=");

    private async Task UploadSignatureAsync(int companyId, long userId)
    {
        await using var sp = Provider(companyId, 1, userId: 1, storageRoot: _root);
        await using var scope = sp.CreateAsyncScope();
        var svc = scope.ServiceProvider.GetRequiredService<Accounting.Application.Attachments.IAttachmentService>();
        await svc.UploadAsync("USER_SIGNATURE", userId, "OTHER", "sig",
            "sig.png", "image/png", MinimalPng.Length, new MemoryStream(MinimalPng), default);
    }

    private static void AssertPdf(byte[] bytes)
    {
        bytes.Should().NotBeNull();
        bytes.Length.Should().BeGreaterThan(1024);
        System.Text.Encoding.ASCII.GetString(bytes, 0, 5).Should().Be("%PDF-");
    }

    // ═══════════════════ T1/T2 (I2, I12) — Draft vs Posted resolution ══════════════

    [SkippableFact]
    public async Task T1_posted_TI_with_signature_and_position_resolves_the_left_box()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var co = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true);
        var actorId = await CreateActorUserAsync(co.CompanyId, "Somchai Signer", "Sales Manager");

        // storageRoot: _root — BuildPaperAsync reads the uploaded signature file back, so this
        // provider's storage must point at the SAME root UploadSignatureAsync wrote to.
        await using var sp = Provider(co.CompanyId, co.BranchId, actorId, storageRoot: _root);
        await using var scope = sp.CreateAsyncScope();
        var svc = scope.ServiceProvider.GetRequiredService<ITaxInvoiceService>();
        var tiId = await svc.CreateDraftAsync(new CreateTaxInvoiceRequest(
            Today, co.CustomerId, false, "THB", 1m, null, null, null,
            [new TaxInvoiceLineInput(null, null, "T1 line", 1m, 1, "หน่วย", 1000m, 0m, 1, "VAT7", 0.07m)],
            null), default);

        (await svc.BuildPaperAsync(tiId, default)).Signatures.Should().BeNull("a Draft has no signer yet (I2)");

        await svc.PostAsync(tiId, default);
        await UploadSignatureAsync(co.CompanyId, actorId);

        var model = await svc.BuildPaperAsync(tiId, default);
        model.Signatures.Should().NotBeNull();
        model.Signatures!.LeftBytes.Should().NotBeNull();
        model.Signatures.LeftUrl.Should().MatchRegex(@"^/attachments/\d+/download$");
        model.Signatures.LeftPosition.Should().Be("Sales Manager");
        model.Signatures.LeftName.Should().Be("Somchai Signer");
    }

    [SkippableFact]
    public async Task T2_posted_TI_whose_actor_has_no_signature_or_position_still_resolves_the_name()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var co = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true);
        var actorId = await CreateActorUserAsync(co.CompanyId, "No Signature Guy", position: null);

        await using var sp = Provider(co.CompanyId, co.BranchId, actorId);
        await using var scope = sp.CreateAsyncScope();
        var svc = scope.ServiceProvider.GetRequiredService<ITaxInvoiceService>();
        var tiId = await svc.CreateDraftAsync(new CreateTaxInvoiceRequest(
            Today, co.CustomerId, false, "THB", 1m, null, null, null,
            [new TaxInvoiceLineInput(null, null, "T2 line", 1m, 1, "หน่วย", 1000m, 0m, 1, "VAT7", 0.07m)],
            null), default);
        await svc.PostAsync(tiId, default);

        var model = await svc.BuildPaperAsync(tiId, default);
        model.Signatures.Should().NotBeNull("a signer exists even with no image/position");
        model.Signatures!.LeftBytes.Should().BeNull();
        model.Signatures.LeftPosition.Should().BeNull();
        model.Signatures.LeftName.Should().Be("No Signature Guy", "§A4 fires on the name line independently of the image");

        AssertPdf(await svc.BuildPdfAsync(tiId, default));
    }

    // ═══════════════════════ T3 (I5) — PV three-box, stamp on middle ═══════════════

    [SkippableFact]
    public async Task T3_payment_voucher_stamps_the_middle_box_and_never_the_right()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        const int companyId = 1;
        var posterId = await CreateActorUserAsync(companyId, "Poster Person", "Clerk");
        var approverId = await CreateActorUserAsync(companyId, "Approver Person", "Manager");

        await using var creator = Provider(companyId, 1, posterId);
        long vid;
        int catId; long expAcct;
        await using (var s = creator.CreateAsyncScope())
        {
            var db = s.ServiceProvider.GetRequiredService<AccountingDbContext>();
            var v = new Vendor
            {
                CompanyId = companyId, VendorCode = TestIds.VendorCode(), NameTh = "PV Vendor T3",
                TaxId = TestIds.TaxId(), BranchCode = "00000",
                VendorType = CustomerType.Corporate, IsForeign = false, VatRegistered = true,
            };
            db.Vendors.Add(v);
            var acctId = await db.ChartOfAccounts.Where(a => a.CompanyId == companyId && a.AccountCode == "5200")
                .Select(a => a.AccountId).FirstAsync();
            var cat = new ExpenseCategory
            {
                CompanyId = companyId, CategoryCode = TestIds.ExpenseCategoryCode(),
                NameTh = "T3 category", DefaultExpenseAccountId = acctId, DefaultIsRecoverableVat = true,
            };
            db.ExpenseCategories.Add(cat);
            await db.SaveChangesAsync();
            vid = v.VendorId; catId = cat.CategoryId; expAcct = acctId;
        }

        long pvId;
        await using (var s = creator.CreateAsyncScope())
        {
            var svc = s.ServiceProvider.GetRequiredService<IPaymentVoucherService>();
            pvId = await svc.CreateDraftAsync(new CreatePaymentVoucherRequest(
                DocDate: Today, VendorId: vid, ExpenseCategoryId: catId,
                PaymentMethod: PaymentMethod.Transfer, ChequeNo: null, ChequeDate: null,
                BankAccountId: null, CurrencyCode: "THB", ExchangeRate: 1m,
                Description: "T3", Notes: null,
                Lines: [new PaymentVoucherLineInput(expAcct, "T3 line", 1000m, null, 0.07m, true, null, 0m)]),
                default);

            var draft = await svc.BuildPaperAsync(pvId, default);
            draft.Signatures.Should().BeNull();
        }

        // Approve (middle box actor) with a DIFFERENT user than the poster below, so T3 can
        // prove the two boxes resolve independently.
        await using (var s = Provider(companyId, 1, approverId).CreateAsyncScope())
            await s.ServiceProvider.GetRequiredService<IPaymentVoucherService>().ApproveAsync(pvId, default);

        await using (var s = Provider(companyId, 1, posterId).CreateAsyncScope())
            await s.ServiceProvider.GetRequiredService<IPaymentVoucherService>().PostAsync(pvId, default);

        await UploadSignatureAsync(companyId, approverId);

        // storageRoot: _root — must match UploadSignatureAsync's write target so BuildPaperAsync
        // can read the file back (storage-footgun).
        await using var reader = Provider(companyId, 1, posterId, storageRoot: _root).CreateAsyncScope();
        var readSvc = reader.ServiceProvider.GetRequiredService<IPaymentVoucherService>();
        var model = await readSvc.BuildPaperAsync(pvId, default);

        model.Signatures.Should().NotBeNull();
        model.Signatures!.StampOnMiddle.Should().BeTrue("the PV stamp accompanies the approver's box (§A2)");
        model.Signatures.MiddleBytes.Should().NotBeNull("the approver uploaded a signature");
        model.Signatures.MiddleName.Should().Be("Approver Person");
        model.Signatures.LeftName.Should().Be("Poster Person");
        // Structural (I5): the record exposes no right-box field at all — the counterparty
        // box cannot be touched by construction.
        typeof(Accounting.Application.Pdf.PaperSignatures).GetProperty("RightName").Should().BeNull();
        typeof(Accounting.Application.Pdf.PaperSignatures).GetProperty("RightBytes").Should().BeNull();

        AssertPdf(await readSvc.BuildPdfAsync(pvId, default));
    }

    // ═══════════════════════ T4 (I8) — decorative, never fails a document ══════════

    [SkippableFact]
    public async Task T4_a_missing_file_or_disallowed_mime_never_fails_the_document()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var co = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true);
        var actorId = await CreateActorUserAsync(co.CompanyId, "T4 Actor");

        await using var sp = Provider(co.CompanyId, co.BranchId, actorId, storageRoot: _root);
        await using var scope = sp.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AccountingDbContext>();
        var svc = scope.ServiceProvider.GetRequiredService<ITaxInvoiceService>();

        var tiId = await svc.CreateDraftAsync(new CreateTaxInvoiceRequest(
            Today, co.CustomerId, false, "THB", 1m, null, null, null,
            [new TaxInvoiceLineInput(null, null, "T4 line", 1m, 1, "หน่วย", 1000m, 0m, 1, "VAT7", 0.07m)],
            null), default);
        await svc.PostAsync(tiId, default);

        // (a) a row pointing at a NONEXISTENT file.
        db.Attachments.Add(new Attachment
        {
            CompanyId = co.CompanyId, ParentType = AttachmentParentType.UserSignature, ParentId = actorId,
            Category = AttachmentCategory.Other, FileName = "ghost.png", MimeType = "image/png", SizeBytes = 4,
            StoragePath = "does/not/exist.png", UploadedAt = DateTimeOffset.UtcNow, UploadedBy = 1,
            Description = "T4 missing file",
        });
        await db.SaveChangesAsync();

        var model1 = await svc.BuildPaperAsync(tiId, default);
        model1.Signatures.Should().NotBeNull("a signer exists");
        model1.Signatures!.LeftBytes.Should().BeNull("the file read must fail silently (I8)");
        AssertPdf(await svc.BuildPdfAsync(tiId, default));

        // (b) a disallowed MIME (application/pdf) — even if the file existed, the resolver's
        // own allowlist rejects it before ever attempting a read.
        db.Attachments.Add(new Attachment
        {
            CompanyId = co.CompanyId, ParentType = AttachmentParentType.UserSignature, ParentId = actorId,
            Category = AttachmentCategory.Other, FileName = "doc.pdf", MimeType = "application/pdf", SizeBytes = 4,
            StoragePath = "also/does/not/exist.pdf", UploadedAt = DateTimeOffset.UtcNow.AddSeconds(1), UploadedBy = 1,
            Description = "T4 bad mime",
        });
        await db.SaveChangesAsync();

        var model2 = await svc.BuildPaperAsync(tiId, default);
        model2.Signatures!.LeftBytes.Should().BeNull("disallowed MIME must never reach storage.OpenReadAsync");
        AssertPdf(await svc.BuildPdfAsync(tiId, default));

        // (c) Tier-2 finding (2026-07-30, MED) — an ALLOWED mime (image/png) recorded, a REAL
        // file on disk at the referenced path, but the file's actual BYTES are not a decodable
        // image (a spoofed Content-Type at upload time, or corruption at rest). Before the magic-
        // number check this would reach QuestPDF's .Image() and THROW (bricking GET /pdf for
        // every document this actor signs) — the render must instead render an empty box.
        var corruptPath = "corrupt.png";
        Directory.CreateDirectory(_root);
        await File.WriteAllBytesAsync(Path.Combine(_root, corruptPath), [1, 2, 3, 4]);
        db.Attachments.Add(new Attachment
        {
            CompanyId = co.CompanyId, ParentType = AttachmentParentType.UserSignature, ParentId = actorId,
            Category = AttachmentCategory.Other, FileName = "corrupt.png", MimeType = "image/png", SizeBytes = 4,
            StoragePath = corruptPath, UploadedAt = DateTimeOffset.UtcNow.AddSeconds(2), UploadedBy = 1,
            Description = "T4 corrupt bytes with an allowed mime",
        });
        await db.SaveChangesAsync();

        var model3 = await svc.BuildPaperAsync(tiId, default);
        model3.Signatures!.LeftBytes.Should().BeNull(
            "bytes with no valid image magic number must never reach the renderer, even with an allowed MIME (I8)");
        AssertPdf(await svc.BuildPdfAsync(tiId, default));
    }

    // ═══════════════════ T5 — all TEN kinds round-trip without throwing ════════════

    [SkippableFact]
    public async Task T5_all_ten_document_kinds_round_trip_BuildPaperAsync_unsigned_and_signed()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var co = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true);
        var actorId = await CreateActorUserAsync(co.CompanyId, "T5 Chain Actor");
        await using var sp = Provider(co.CompanyId, co.BranchId, actorId);

        var line = new ChainLineInput(null, "T5 chain line", 2m, "ชิ้น", 500m, 0m, 1, "VAT7", 0.07m);

        // 1-3: Quotation -> Sales Order -> Delivery Order.
        long qId, soId, doId;
        await using (var s = sp.CreateAsyncScope())
        {
            var qsvc = s.ServiceProvider.GetRequiredService<IQuotationService>();
            var pdfsvc = s.ServiceProvider.GetRequiredService<ISalesChainPdfService>();
            qId = await qsvc.CreateDraftAsync(new CreateQuotationRequest(
                Today, Today.AddDays(30), co.CustomerId, null, "THB", 1m, null, null, [line]), default);
            (await pdfsvc.QuotationPaperAsync(qId, default)).Signatures.Should().BeNull();
            await qsvc.SendAsync(qId, default);
            (await pdfsvc.QuotationPaperAsync(qId, default)).ValidUntil.Should().NotBeNull("Quotation's ยืนราคาถึง (§1.2)");
            await qsvc.AcceptAsync(qId, default);
            soId = await qsvc.ConvertToSalesOrderAsync(qId, default);
        }
        await using (var s = sp.CreateAsyncScope())
        {
            var sosvc = s.ServiceProvider.GetRequiredService<ISalesOrderService>();
            var pdfsvc = s.ServiceProvider.GetRequiredService<ISalesChainPdfService>();
            await sosvc.PostAsync(soId, default);
            var model = await pdfsvc.SalesOrderPaperAsync(soId, default);
            model.Signatures.Should().NotBeNull();
            doId = await sosvc.CreateDeliveryOrderAsync(soId, new CreateDeliveryOrderRequest(
                Today, co.CustomerId, null, IsCombinedWithTi: false, null, soId,
                [new DeliveryLineInput(null, null, "T5 chain line", 2m, "ชิ้น", 500m, 0m, 1, "VAT7", 0.07m)]),
                default);
        }
        await using (var s = sp.CreateAsyncScope())
        {
            var dosvc = s.ServiceProvider.GetRequiredService<IDeliveryOrderService>();
            var pdfsvc = s.ServiceProvider.GetRequiredService<ISalesChainPdfService>();
            await dosvc.IssueAsync(doId, default);
            (await pdfsvc.DeliveryOrderPaperAsync(doId, default)).Signatures.Should().NotBeNull();
        }

        // 4: Billing Note (Invoice).
        long bnId;
        await using (var s = sp.CreateAsyncScope())
        {
            var bnsvc = s.ServiceProvider.GetRequiredService<IBillingNoteService>();
            bnId = await bnsvc.CreateFromDeliveryOrderAsync(doId, default);
            (await bnsvc.BuildPaperAsync(bnId, default)).Signatures.Should().BeNull();
            await bnsvc.IssueAsync(bnId, default);
            var bnModel = await bnsvc.BuildPaperAsync(bnId, default);
            bnModel.Signatures.Should().NotBeNull();
            bnModel.ValidUntilLabel.Should().Be("ครบกำหนดชำระ", "Billing Note's distinguishing label (§1.2)");
        }

        // 5: Tax Invoice.
        long tiId;
        await using (var s = sp.CreateAsyncScope())
        {
            var tisvc = s.ServiceProvider.GetRequiredService<ITaxInvoiceService>();
            tiId = await tisvc.CreateFromBillingNoteAsync(bnId, default);
            await tisvc.PostAsync(tiId, default);
            (await tisvc.BuildPaperAsync(tiId, default)).Signatures.Should().NotBeNull();
        }

        // 6: Receipt.
        long rcId;
        await using (var s = sp.CreateAsyncScope())
        {
            var rsvc = s.ServiceProvider.GetRequiredService<IReceiptService>();
            rcId = await rsvc.CreateDraftAsync(new CreateReceiptRequest(
                Today, co.CustomerId, PaymentMethod.Transfer, null, null, null, "THB", 1m, null,
                [new ReceiptApplicationInput(tiId, 1070m)], null), default);
            await rsvc.PostAsync(rcId, default);
            (await rsvc.BuildPaperAsync(rcId, default)).Signatures.Should().NotBeNull();
        }

        // 7-8: Credit Note + Debit Note (share one entity/service; both need a source TI —
        // create a second TI to adjust against so a CN doesn't collide with the one Receipt applied to).
        long tiId2;
        await using (var s = sp.CreateAsyncScope())
        {
            var tisvc = s.ServiceProvider.GetRequiredService<ITaxInvoiceService>();
            tiId2 = await tisvc.CreateDraftAsync(new CreateTaxInvoiceRequest(
                Today, co.CustomerId, false, "THB", 1m, null, null, null,
                [new TaxInvoiceLineInput(null, null, "T5 second TI", 1m, 1, "งาน", 2000m, 0m, 1, "VAT7", 0.07m)],
                null), default);
            await tisvc.PostAsync(tiId2, default);
        }
        await using (var s = sp.CreateAsyncScope())
        {
            var nsvc = s.ServiceProvider.GetRequiredService<ITaxAdjustmentNoteService>();
            var cnId = await nsvc.CreateDraftAsync(new CreateTaxAdjustmentNoteRequest(
                TaxAdjustmentNoteType.Credit, Today, tiId2, "RETURN", "T5 credit note reason",
                200m, 0.07m, "THB", 1m, null), default);
            (await nsvc.BuildPaperAsync(cnId, default)).Signatures.Should().BeNull();
            await nsvc.PostAsync(cnId, default);
            var cnModel = await nsvc.BuildPaperAsync(cnId, default);
            cnModel.Signatures.Should().NotBeNull();
            cnModel.Items.Should().ContainSingle("CN/DN synthesize ONE line (reason + adjusted value, §1.2)");

            var dnId = await nsvc.CreateDraftAsync(new CreateTaxAdjustmentNoteRequest(
                TaxAdjustmentNoteType.Debit, Today, tiId2, "PRICE_CHANGE", "T5 debit note reason",
                150m, 0.07m, "THB", 1m, null), default);
            await nsvc.PostAsync(dnId, default);
            (await nsvc.BuildPaperAsync(dnId, default)).Signatures.Should().NotBeNull();
        }

        // 9: Purchase Order.
        long poId;
        await using (var s = sp.CreateAsyncScope())
        {
            var db = s.ServiceProvider.GetRequiredService<AccountingDbContext>();
            var v = new Vendor
            {
                CompanyId = co.CompanyId, VendorCode = TestIds.VendorCode(), NameTh = "T5 PO Vendor",
                TaxId = TestIds.TaxId(), BranchCode = "00000",
                VendorType = CustomerType.Corporate, IsForeign = false, VatRegistered = true,
            };
            db.Vendors.Add(v);
            await db.SaveChangesAsync();

            var posvc = s.ServiceProvider.GetRequiredService<IPurchaseOrderService>();
            poId = await posvc.CreateDraftAsync(new CreatePurchaseOrderRequest(
                Today, Today.AddDays(30), v.VendorId, null, "THB", 1m, "T5 PO", null,
                [new PurchaseOrderLineInput(null, "T5 PO line", 5m, "ชิ้น", 200m, 0m, 1, "VAT7", 0.07m, null)]),
                default);
            (await posvc.BuildPaperAsync(poId, default)).Signatures.Should().BeNull();
            await posvc.ApproveAsync(poId, default);
            var poModel = await posvc.BuildPaperAsync(poId, default);
            poModel.Signatures.Should().NotBeNull();
            poModel.PartyLabel!.Th.Should().Be("ผู้ขาย", "PO's partyLabel override (§1.2)");
        }

        // 10: Payment Voucher.
        await using (var s = sp.CreateAsyncScope())
        {
            var db = s.ServiceProvider.GetRequiredService<AccountingDbContext>();
            var v = new Vendor
            {
                CompanyId = co.CompanyId, VendorCode = TestIds.VendorCode(), NameTh = "T5 PV Vendor",
                TaxId = TestIds.TaxId(), BranchCode = "00000",
                VendorType = CustomerType.Corporate, IsForeign = false, VatRegistered = true,
            };
            db.Vendors.Add(v);
            var acctId = await db.ChartOfAccounts.Where(a => a.CompanyId == co.CompanyId && a.AccountCode == "5200")
                .Select(a => a.AccountId).FirstAsync();
            var cat = new ExpenseCategory
            {
                CompanyId = co.CompanyId, CategoryCode = TestIds.ExpenseCategoryCode(),
                NameTh = "T5 category", DefaultExpenseAccountId = acctId, DefaultIsRecoverableVat = true,
            };
            db.ExpenseCategories.Add(cat);
            await db.SaveChangesAsync();

            var pvsvc = s.ServiceProvider.GetRequiredService<IPaymentVoucherService>();
            var pvId = await pvsvc.CreateDraftAsync(new CreatePaymentVoucherRequest(
                DocDate: Today, VendorId: v.VendorId, ExpenseCategoryId: cat.CategoryId,
                PaymentMethod: PaymentMethod.Transfer, ChequeNo: null, ChequeDate: null,
                BankAccountId: null, CurrencyCode: "THB", ExchangeRate: 1m,
                Description: "T5 PV", Notes: null,
                Lines: [new PaymentVoucherLineInput(acctId, "T5 PV line", 1000m, null, 0.07m, true, null, 0m)]),
                default);
            (await pvsvc.BuildPaperAsync(pvId, default)).Signatures.Should().BeNull();
            await pvsvc.ApproveAsync(pvId, default);
            await pvsvc.PostAsync(pvId, default);
            var pvModel = await pvsvc.BuildPaperAsync(pvId, default);
            pvModel.Signatures.Should().NotBeNull();
            pvModel.Signatures!.StampOnMiddle.Should().BeTrue("PV's Middle sign role + stamp (§1.2)");
        }
    }

    // ═══════════════════════ T6/T7/T8 (I4, I6) — pagination ════════════════════════

    private async Task<long> PostTiWithLinesAsync(ServiceProvider sp, long customerId, int lineCount)
    {
        await using var s = sp.CreateAsyncScope();
        var svc = s.ServiceProvider.GetRequiredService<ITaxInvoiceService>();
        var lines = Enumerable.Range(1, lineCount)
            .Select(i => new TaxInvoiceLineInput(null, null, $"Line {i} of pagination test", 1m, 1,
                "หน่วย", 1000m, 0m, 1, "VAT7", 0.07m))
            .ToList();
        var id = await svc.CreateDraftAsync(new CreateTaxInvoiceRequest(
            Today, customerId, false, "THB", 1m, null, null, null, lines, null), default);
        await svc.PostAsync(id, default);
        return id;
    }

    [SkippableFact]
    public async Task T6_a_3_line_posted_TI_renders_exactly_1_page()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var co = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true);
        await using var sp = Provider(co.CompanyId, co.BranchId, userId: 1);
        var tiId = await PostTiWithLinesAsync(sp, co.CustomerId, 3);

        await using var s = sp.CreateAsyncScope();
        var bytes = await s.ServiceProvider.GetRequiredService<ITaxInvoiceService>().BuildPdfAsync(tiId, default);
        using var doc = PdfDocument.Open(bytes);
        // Baseline (captured 2026-07-30 against the PRE-restructure renderer, git HEAD
        // Y:\...\Pdf\PaperDocumentPdf.cs, via a throwaway probe — see attempt log): a 3-line
        // Draft-shaped TI (before this spec's page.Header()/page.Footer() split) rendered
        // exactly 1 page. Confirmed IDENTICAL page count and byte-for-byte-identical extracted
        // text (T9) after the restructure, plus the new "หน้า 1 / 1" footer appended.
        doc.NumberOfPages.Should().Be(1, "I6 — a document that fit on one page before must still fit");
    }

    // Tier-2 finding (2026-07-30, LOW, I6) — T6 only exercised the UNSIGNED growth path
    // (slotH stays 26pt, no ตำแหน่ง line). Repeat the SAME 3-line fixture fully signed —
    // signature image + company stamp + ตำแหน่ง — the actual growth case (slotH 26->46pt,
    // §D1, PLUS the new position line, §A3) — and confirm it STILL fits on 1 page.
    [SkippableFact]
    public async Task T6b_the_same_3_line_TI_signed_with_signature_stamp_and_position_still_renders_1_page()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var co = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true);
        var actorId = await CreateActorUserAsync(co.CompanyId, "T6b Actor", "Signing Manager");

        await using var sp = Provider(co.CompanyId, co.BranchId, actorId, storageRoot: _root);
        var tiId = await PostTiWithLinesAsync(sp, co.CustomerId, 3);
        await UploadSignatureAsync(co.CompanyId, actorId);

        await using (var stampScope = Provider(co.CompanyId, co.BranchId, userId: 1, storageRoot: _root).CreateAsyncScope())
            await stampScope.ServiceProvider.GetRequiredService<ICompanyProfileService>()
                .UpdateStampAsync("stamp.png", "image/png", MinimalPng.Length, new MemoryStream(MinimalPng), default);

        await using var s = sp.CreateAsyncScope();
        var bytes = await s.ServiceProvider.GetRequiredService<ITaxInvoiceService>().BuildPdfAsync(tiId, default);
        using var doc = PdfDocument.Open(bytes);
        doc.NumberOfPages.Should().Be(1,
            "I6 — the signed-doc growth path (slotH 26pt->46pt, §D1, plus the new ตำแหน่ง line, " +
            "§A3) must not push a previously-1-page document to 2 pages");
    }

    [SkippableFact]
    public async Task T7_a_30_line_TI_renders_2_pages_with_totals_on_page_2_only()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var co = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true);
        await using var sp = Provider(co.CompanyId, co.BranchId, userId: 1);
        var tiId = await PostTiWithLinesAsync(sp, co.CustomerId, 30);

        await using var s = sp.CreateAsyncScope();
        var tisvc = s.ServiceProvider.GetRequiredService<ITaxInvoiceService>();
        var bytes = await tisvc.BuildPdfAsync(tiId, default);
        using var doc = PdfDocument.Open(bytes);
        doc.NumberOfPages.Should().Be(2, "30 lines must overflow a single page (I4)");

        var detail = await tisvc.GetDetailAsync(tiId, default);
        // Grand-total numeral, mark-free — §1.9.4 forbids asserting Thai text with tone marks.
        var totalNumeral = detail!.TotalAmount.ToString("N2", System.Globalization.CultureInfo.GetCultureInfo("th-TH"));
        var page1 = doc.GetPage(1).Text;
        var page2 = doc.GetPage(2).Text;
        page2.Should().Contain(totalNumeral, "the price summary (bottom group) must land on page 2");
        page1.Should().NotContain(totalNumeral, "the bottom group must never split across pages (I4)");
    }

    [SkippableFact]
    public async Task T8_page_numbering_and_repeated_header_on_a_2_page_document()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var co = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true);
        await using var sp = Provider(co.CompanyId, co.BranchId, userId: 1);
        var tiId = await PostTiWithLinesAsync(sp, co.CustomerId, 30);

        await using var s = sp.CreateAsyncScope();
        var tisvc = s.ServiceProvider.GetRequiredService<ITaxInvoiceService>();
        var bytes = await tisvc.BuildPdfAsync(tiId, default);
        using var doc = PdfDocument.Open(bytes);
        doc.NumberOfPages.Should().Be(2);

        var detail = await tisvc.GetDetailAsync(tiId, default);
        var page1 = doc.GetPage(1).Text;
        var page2 = doc.GetPage(2).Text;

        page1.Should().Contain("/ 2");
        page2.Should().Contain("/ 2");
        // Seller tax id (a digit string from Head) proves the header repeated onto page 2. The
        // rendered form is dash-formatted ("0-0007-...") — strip dashes from both sides so the
        // digit sequence is checked as a contiguous run regardless of formatting.
        var taxIdDigits = new string((detail!.SupplierTaxId ?? "").Where(char.IsDigit).ToArray());
        taxIdDigits.Should().NotBeEmpty();
        page2.Replace("-", "").Should().Contain(taxIdDigits, "Head() (incl. seller tax id) must repeat on page 2 (§C2)");
    }

    // ═══════════════════════ T9 (I1, I12) — styling freeze ═════════════════════════

    // Baseline captured 2026-07-30 by temporarily reverting PaperDocumentPdf.cs to git HEAD
    // (the pre-WP-3 renderer) and rendering a Draft Tax Invoice (company 1's demo profile,
    // customer C-DEMO-001, one line "T9 baseline probe line" x1 @ 1,000.00) through
    // PdfPig-extracted text, with the one per-render-varying element (the DD/MM/YYYY Buddhist
    // date) normalized to a placeholder so the pin is stable across calendar days. Re-ran the
    // IDENTICAL probe against the NEW (post-restructure) renderer: byte-for-byte identical
    // EXCEPT for the new "หน้า 1 / 1" footer appended at the end (expected, §C3) — proving I1
    // (styling freeze) holds for a Draft document.
    // Captured 2026-07-30 by rendering the SAME fixture (company 1 demo profile, customer
    // C-DEMO-001, one line "T9 baseline probe line" x1 @ 1,000.00) through the CURRENT
    // (post-restructure) renderer, then through NormalizeForComparison — confirmed byte-stable
    // across 3 consecutive runs (Thai combining-mark extraction is otherwise non-deterministic,
    // §1.9.4: one run emitted a plain space and another a literal NUL at the same dropped-mark
    // position — NormalizeForComparison strips both classes of noise). Content equivalence with
    // the PRE-restructure renderer was separately verified by a throwaway git-HEAD probe (see
    // attempt log): identical except for the newly-appended "หน้า 1 / 1" page-footer (§C3).
    // Already includes that footer (normalized), so this constant is compared AS-IS.
    private const string T9PreChangeNormalizedText =
        "Demo Company (เดโม)1 อาคารเดโม ชน 1 ถนนสาทร แขวงทงมหาเมฆ ทงมหาเมฆ เขตสาทร กรงเทพมหานคร 10120" +
        "เลขประจาตวผเสยภาษ: 0-0000-00000-00-0 · สาขา 00000ใบกากบภาษTAX INVOICE—ลกคา / Customer" +
        "ลกคาทดสอบ จากด99 ถ.ทดสอบ กรงเทพฯ 10110เลขประจาตวผเสยภาษ: 0-1055-56123-45-3สาขา: 00000" +
        "วนท / DateDD/MM/YYYY#รายการ / Descriptionจานวนหนวยราคา/หนวยจานวนเงน1T9 baseline probe line1" +
        "หนวย1,000.001,000.00มลคากอนหกสวนลดSubtotal1,000.00มลคากอนภาษBefore VAT1,000.00" +
        "ภาษมลคาเพม 7%VAT70.00จานวนเงนรวมทงสนGrand Total฿ 1,070.00(หนงพนเจดสบบาทถวน)" +
        "ลงชอ ผออกใบกากบ( Demo Company (เดโม) )วนท ____ / ____ / ______" +
        "ลงชอ ผซอ( ลกคาทดสอบ จากด )วนท ____ / ____ / ______หนา 1 / 1";

    [SkippableFact]
    public async Task T9_styling_freeze_a_draft_documents_extracted_text_is_unchanged()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        // Company 1's demo profile/customer — the SAME fixture the baseline above was captured
        // against (name/address/tax-id must match verbatim for the pinned string to apply).
        await using var sp = Provider(1, 1, userId: 1);
        await using var s = sp.CreateAsyncScope();
        var db = s.ServiceProvider.GetRequiredService<AccountingDbContext>();
        var cust = await db.Customers.Where(c => c.CustomerCode == "C-DEMO-001")
            .Select(c => c.CustomerId).FirstAsync();
        var svc = s.ServiceProvider.GetRequiredService<ITaxInvoiceService>();
        var id = await svc.CreateDraftAsync(new CreateTaxInvoiceRequest(
            Today, cust, false, "THB", 1m, null, null, null,
            [new TaxInvoiceLineInput(null, null, "T9 baseline probe line", 1m, 3, "หน่วย", 1000m, 0m, 1, "VAT7", 0.07m)],
            null), default);
        // Fixture MUST be a Draft (§9) — a signed document legitimately changes the name line (§A4, T17).

        var bytes = await svc.BuildPdfAsync(id, default);
        using var doc = PdfDocument.Open(bytes);
        doc.NumberOfPages.Should().Be(1, "I6 — unchanged vertical budget");

        var normalized = NormalizeForComparison(doc.GetPage(1).Text);
        // Byte equality is forbidden (§1.9.3); text equality (page-for-page) is the honest
        // substitute.
        normalized.Should().Be(T9PreChangeNormalizedText);
    }

    /// <summary>§1.9.4 — PDF text extraction of Thai COMBINING marks is not deterministic run to
    /// run (confirmed empirically 2026-07-30: the identical renderer produced a plain space in
    /// one extraction and a literal NUL (U+0000) in another, at the exact position a dropped
    /// tone/vowel mark left a gap). Strip all Unicode nonspacing marks (Mn) and control
    /// characters, then collapse whitespace, so an exact-equality comparison is robust to this
    /// known instability while still proving every OTHER character (labels, numerals, English
    /// text, word order) is unchanged. Also normalizes away the one per-render-varying element
    /// (the Buddhist-calendar date) so the pin is stable across calendar days.</summary>
    private static string NormalizeForComparison(string text)
    {
        var sb = new System.Text.StringBuilder(text.Length);
        foreach (var c in text)
        {
            if (char.IsControl(c)) continue;
            if (System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c)
                == System.Globalization.UnicodeCategory.NonSpacingMark) continue;
            sb.Append(c);
        }
        var collapsed = System.Text.RegularExpressions.Regex.Replace(sb.ToString(), @"\s+", " ").Trim();
        return System.Text.RegularExpressions.Regex.Replace(collapsed, @"\d{2}/\d{2}/\d{4}", "DD/MM/YYYY");
    }

    // ═══════════════════════ T17 (§A4, I12) — name line end to end ═════════════════

    [SkippableFact]
    public async Task T17_name_line_switches_from_company_to_signer_and_the_counterparty_never_changes()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var co = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true);

        // Distinct ASCII strings (test-controlled, no Thai tone marks — §1.9.4). The
        // counterparty (customer) is ALSO given an ASCII name here — comparing PDF-extracted
        // text against a Thai DB string (e.g. TestCompanyFactory's default "ลูกค้าทดสอบ จำกัด")
        // would hit the same combining-mark extraction instability T9 worked around (§1.9.4).
        const string companyName = "Acme Trading Co Ltd";
        const string signerName = "Somchai Signer";
        const string customerName = "Wayne Enterprises Ltd";
        long customerId;
        // TaxInvoiceService.CreateDraftAsync snapshots SupplierName from company.NameTh (the
        // master.companies legal name, per ม.86/4 — NOT CompanyProfile.TradeName), so THAT is
        // the field to control here.
        await using (var setup = Provider(co.CompanyId, co.BranchId, userId: 1).CreateAsyncScope())
        {
            var db = setup.ServiceProvider.GetRequiredService<AccountingDbContext>();
            var company = await db.Companies.FirstAsync(c => c.CompanyId == co.CompanyId);
            company.NameTh = companyName;

            var customer = new Customer
            {
                CompanyId = co.CompanyId, CustomerCode = TestIds.CustomerCode(),
                CustomerType = CustomerType.Corporate, NameTh = customerName,
                TaxId = TestIds.TaxId(), BranchCode = "00000", BillingAddress = "123 Gotham St",
                CreditLimit = 0, PaymentTermDays = 30, IsActive = true, CreatedAt = DateTimeOffset.UtcNow,
            };
            db.Customers.Add(customer);
            await db.SaveChangesAsync();
            customerId = customer.CustomerId;
        }
        var actorId = await CreateActorUserAsync(co.CompanyId, signerName);

        await using var sp = Provider(co.CompanyId, co.BranchId, actorId);
        await using var s = sp.CreateAsyncScope();
        var svc = s.ServiceProvider.GetRequiredService<ITaxInvoiceService>();
        var tiId = await svc.CreateDraftAsync(new CreateTaxInvoiceRequest(
            Today, customerId, false, "THB", 1m, null, null, null,
            [new TaxInvoiceLineInput(null, null, "T17 line", 1m, 1, "unit", 1000m, 0m, 1, "VAT7", 0.07m)],
            null), default);

        // 1. Draft -> company name in the parenthesised form; NOT the signer's name.
        var draftText = PdfDocument.Open(await svc.BuildPdfAsync(tiId, default)).GetPage(1).Text;
        draftText.Should().Contain($"( {companyName} )");
        draftText.Should().NotContain($"( {signerName} )");

        // 2. Posted, actor resolves -> the signer's PERSON name replaces it on that line.
        await svc.PostAsync(tiId, default);
        var postedText = PdfDocument.Open(await svc.BuildPdfAsync(tiId, default)).GetPage(1).Text;
        postedText.Should().Contain($"( {signerName} )");
        postedText.Should().NotContain($"( {companyName} )", "the parenthesised form must show the signer, not the company");

        // 3. Counterparty name present and UNCHANGED across both states (I5).
        var detail = await svc.GetDetailAsync(tiId, default);
        draftText.Should().Contain(detail!.CustomerName);
        postedText.Should().Contain(detail.CustomerName);
    }

    [SkippableFact]
    public async Task T17_payment_voucher_middle_box_shows_the_approvers_name_once_approved()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        const int companyId = 1;
        const string approverName = "Approver Person T17";
        var posterId = await CreateActorUserAsync(companyId, "Poster Person T17");
        var approverId = await CreateActorUserAsync(companyId, approverName);

        await using var creator = Provider(companyId, 1, posterId);
        long vid; int catId; long expAcct;
        await using (var s = creator.CreateAsyncScope())
        {
            var db = s.ServiceProvider.GetRequiredService<AccountingDbContext>();
            var v = new Vendor
            {
                CompanyId = companyId, VendorCode = TestIds.VendorCode(), NameTh = "T17 PV Vendor",
                TaxId = TestIds.TaxId(), BranchCode = "00000",
                VendorType = CustomerType.Corporate, IsForeign = false, VatRegistered = true,
            };
            db.Vendors.Add(v);
            var acctId = await db.ChartOfAccounts.Where(a => a.CompanyId == companyId && a.AccountCode == "5200")
                .Select(a => a.AccountId).FirstAsync();
            var cat = new ExpenseCategory
            {
                CompanyId = companyId, CategoryCode = TestIds.ExpenseCategoryCode(),
                NameTh = "T17 category", DefaultExpenseAccountId = acctId, DefaultIsRecoverableVat = true,
            };
            db.ExpenseCategories.Add(cat);
            await db.SaveChangesAsync();
            vid = v.VendorId; catId = cat.CategoryId; expAcct = acctId;
        }

        long pvId;
        await using (var s = creator.CreateAsyncScope())
            pvId = await s.ServiceProvider.GetRequiredService<IPaymentVoucherService>().CreateDraftAsync(
                new CreatePaymentVoucherRequest(
                    DocDate: Today, VendorId: vid, ExpenseCategoryId: catId,
                    PaymentMethod: PaymentMethod.Transfer, ChequeNo: null, ChequeDate: null,
                    BankAccountId: null, CurrencyCode: "THB", ExchangeRate: 1m,
                    Description: "T17", Notes: null,
                    Lines: [new PaymentVoucherLineInput(expAcct, "T17 line", 1000m, null, 0.07m, true, null, 0m)]),
                default);

        // Draft -> the middle box is the 30-dot blank (no approver name anywhere).
        byte[] draftBytes;
        await using (var s = creator.CreateAsyncScope())
            draftBytes = await s.ServiceProvider.GetRequiredService<IPaymentVoucherService>().BuildPdfAsync(pvId, default);
        PdfDocument.Open(draftBytes).GetPage(1).Text.Should().NotContain(approverName);

        await using (var s = Provider(companyId, 1, approverId).CreateAsyncScope())
            await s.ServiceProvider.GetRequiredService<IPaymentVoucherService>().ApproveAsync(pvId, default);
        await using (var s = Provider(companyId, 1, posterId).CreateAsyncScope())
            await s.ServiceProvider.GetRequiredService<IPaymentVoucherService>().PostAsync(pvId, default);

        byte[] approvedBytes;
        await using (var s = creator.CreateAsyncScope())
            approvedBytes = await s.ServiceProvider.GetRequiredService<IPaymentVoucherService>().BuildPdfAsync(pvId, default);
        PdfDocument.Open(approvedBytes).GetPage(1).Text.Should().Contain(approverName,
            "once approved, the middle box shows the approver's name where a Draft showed the 30-dot blank");
    }
}
