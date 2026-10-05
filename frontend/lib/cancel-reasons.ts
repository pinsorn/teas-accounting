// cancel-reissue-sales-docs §3.3.4 — declared mirror of BE Accounting.Application/Sales/CancelReasonCodes.cs
// (BE is authoritative and validates). Labels live in messages `cancelDoc.reasons.<CODE>`.
export type CancelKind = 'tax-invoice' | 'receipt' | 'invoice';
export type CancelMode = 'cancel' | 'reissue';

export const CANCEL_REASONS: Record<CancelKind, Record<CancelMode, string[]>> = {
  'tax-invoice': {
    reissue: ['BUYER_DETAILS_ERROR', 'ITEM_DESCRIPTION_ERROR', 'OTHER_PARTICULARS_ERROR'],
    cancel: ['ISSUED_IN_ERROR', 'DUPLICATE', 'SALE_CANCELLED_BEFORE_DELIVERY'],
  },
  receipt: {
    reissue: ['PAYER_DETAILS_ERROR', 'PAYMENT_DETAILS_ERROR', 'OTHER_PARTICULARS_ERROR'],
    cancel: ['ISSUED_IN_ERROR', 'DUPLICATE', 'PAYMENT_NOT_RECEIVED'],
  },
  invoice: {
    reissue: [],
    cancel: ['ISSUED_IN_ERROR', 'DUPLICATE', 'SALE_CANCELLED', 'DETAILS_ERROR'],
  },
};

export const reasonsFor = (kind: CancelKind, mode: CancelMode): string[] => CANCEL_REASONS[kind][mode];
