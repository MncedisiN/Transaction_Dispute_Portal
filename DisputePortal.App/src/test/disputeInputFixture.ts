const sharedTypes = [
  'CardPurchase',
  'CreditCardPayment',
  'DebitOrder',
  'Transfer',
  'CashWithdrawal',
  'Deposit',
  'Fee',
  'Other',
  'Refund',
  'RefundReversal'
]
export const disputeInputFixture = {
  reasonMaximumLength: 120,
  descriptionMinimumLength: 10,
  descriptionMaximumLength: 2000,
  commentMaximumLength: 1000,
  noteMinimumLength: 1,
  noteMaximumLength: 4000,
  statusMaximumLength: 32,
  idempotencyKeyMinimumLength: 8,
  idempotencyKeyMaximumLength: 128,
  categories: [
    {
      code: 'Unrecognized',
      label: 'I Don’t Recognise This Transaction',
      reason: 'I did not make this transaction',
      transactionType: null,
      transactionTypes: [...sharedTypes],
      sortOrder: 1
    },
    {
      code: 'Duplicate',
      label: 'I Was Charged More Than Once',
      reason: 'I was charged more than once',
      transactionType: null,
      transactionTypes: [...sharedTypes],
      sortOrder: 2
    },
    {
      code: 'IncorrectAmount',
      label: 'The Amount Looks Incorrect',
      reason: 'The amount is incorrect',
      transactionType: null,
      transactionTypes: [...sharedTypes],
      sortOrder: 3
    },
    {
      code: 'GoodsOrServices',
      label: 'I Did Not Receive The Goods Or Service',
      reason: 'I did not receive the goods or service',
      transactionType: null,
      transactionTypes: [...sharedTypes],
      sortOrder: 4
    },
    {
      code: 'DebitOrder',
      label: 'I Cancelled This Debit Order',
      reason: 'I cancelled this debit order',
      transactionType: 'DebitOrder',
      transactionTypes: ['DebitOrder'],
      sortOrder: 5
    },
    {
      code: 'Other',
      label: 'Something Else',
      reason: 'Other',
      transactionType: null,
      transactionTypes: [...sharedTypes],
      sortOrder: 6
    }
  ]
}
