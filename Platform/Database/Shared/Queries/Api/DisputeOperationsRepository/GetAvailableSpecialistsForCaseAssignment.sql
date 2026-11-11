{{StaffSelect}}
WHERE "u"."accountStatus" = 'Active'
AND "s"."availability" = 'Available'
AND "r"."code" IN ('dispute_agent', 'fraud_agent', 'card_specialist', 'debit_order_specialist', 'payments_specialist')
