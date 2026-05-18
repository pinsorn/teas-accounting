-- Document prefix registry — 12 standard prefixes (CLAUDE.md §17.4).
INSERT INTO sys.document_prefixes
    (prefix_code, document_type,    description_th,                       description_en,           requires_etax, is_fiscal_doc, is_expense, is_active, created_at)
VALUES
    ('QT',       'QUOTATION',       'ใบเสนอราคา',                          'Quotation',              FALSE, FALSE, FALSE, TRUE, NOW()),
    ('SO',       'SALES_ORDER',     'ใบสั่งขาย',                            'Sales Order',            FALSE, FALSE, FALSE, TRUE, NOW()),
    ('DO',       'DELIVERY_ORDER',  'ใบส่งของ',                            'Delivery Order',         FALSE, FALSE, FALSE, TRUE, NOW()),
    ('TI',       'TAX_INVOICE',     'ใบกำกับภาษี',                          'Tax Invoice',            TRUE,  TRUE,  FALSE, TRUE, NOW()),
    ('RC',       'RECEIPT',         'ใบเสร็จรับเงิน',                       'Receipt',                TRUE,  TRUE,  FALSE, TRUE, NOW()),
    ('CN',       'CREDIT_NOTE',     'ใบลดหนี้',                            'Credit Note',            TRUE,  TRUE,  FALSE, TRUE, NOW()),
    ('DN',       'DEBIT_NOTE',      'ใบเพิ่มหนี้',                          'Debit Note',             TRUE,  TRUE,  FALSE, TRUE, NOW()),
    ('BN',       'BILLING_NOTE',    'ใบวางบิล',                            'Billing Note',           FALSE, FALSE, FALSE, TRUE, NOW()),
    ('RV',       'RECEIPT_VOUCHER', 'ใบสำคัญรับ',                           'Receipt Voucher',        FALSE, TRUE,  FALSE, TRUE, NOW()),
    ('PV',       'PAYMENT_VOUCHER', 'ใบสำคัญจ่าย',                          'Payment Voucher',        FALSE, TRUE,  TRUE,  TRUE, NOW()),
    ('WT',       'WHT_CERT',        'หนังสือรับรองหักภาษี ณ ที่จ่าย',          '50 Tawi',                FALSE, TRUE,  FALSE, TRUE, NOW()),
    ('JV',       'JOURNAL_VOUCHER', 'ใบสำคัญทั่วไป',                         'Journal Voucher',        FALSE, TRUE,  FALSE, TRUE, NOW())
ON CONFLICT (prefix_code) DO NOTHING;
