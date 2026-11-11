<script setup lang="ts">
import DisclosurePanel from './DisclosurePanel.vue'
import { computed } from 'vue'
import type { StaffTransactionDetail } from '../staffTransactions'
import { formatDate, formatMoney, formatBirthDate } from '../utils/format'
import CopyReference from './CopyReference.vue'
const props = defineProps<{ detail: StaffTransactionDetail }>()
const address = computed(() => {
  const a = props.detail.customer.address
  return a
    ? [a.line1, a.line2, a.suburb, a.city, a.province, a.postalCode, a.countryCode]
        .filter(Boolean)
        .join(', ')
    : 'Not supplied'
})
</script>
<template>
  <section
    class="surface-panel assistance-details w3-container w3-margin-bottom w3-section w3-white w3-padding-large w3-round-xlarge w3-border w3-border-light-grey"
    aria-label="Customer And Transaction Details"
  >
    <DisclosurePanel class="customer-account-disclosure">
      <template #heading>Customer &amp; Account</template>
      <div class="w3-responsive">
        <table class="assistance-facts w3-container w3-table w3-bordered">
          <tbody>
            <tr>
              <th
                class="w3-mobile w3-text-grey w3-small"
                scope="row"
              >
                Customer
              </th>
              <td class="w3-mobile">{{ detail.customer.displayName }}</td>
            </tr>

            <tr>
              <th
                class="w3-mobile w3-text-grey w3-small"
                scope="row"
              >
                Email
              </th>
              <td class="w3-mobile">{{ detail.customer.email || 'Not supplied' }}</td>
            </tr>

            <tr>
              <th
                class="w3-mobile w3-text-grey w3-small"
                scope="row"
              >
                Mobile
              </th>
              <td class="w3-mobile">{{ detail.customer.phone || 'Not supplied' }}</td>
            </tr>

            <tr>
              <th
                class="w3-mobile w3-text-grey w3-small"
                scope="row"
              >
                Identity Number
              </th>
              <td class="w3-mobile">{{ detail.customer.identityNumber || 'Not supplied' }}</td>
            </tr>

            <tr>
              <th
                class="w3-mobile w3-text-grey w3-small"
                scope="row"
              >
                Date Of Birth
              </th>
              <td class="w3-mobile">
                {{
                  detail.customer.dateOfBirth
                    ? formatBirthDate(detail.customer.dateOfBirth)
                    : 'Not supplied'
                }}
              </td>
            </tr>

            <tr>
              <th
                class="w3-mobile w3-text-grey w3-small"
                scope="row"
              >
                Address
              </th>
              <td class="w3-mobile">{{ address }}</td>
            </tr>

            <tr>
              <th
                class="w3-mobile w3-text-grey w3-small"
                scope="row"
              >
                Account
              </th>
              <td class="w3-mobile">
                {{ detail.transaction.accountType }} · {{ detail.accountStatus }}
              </td>
            </tr>

            <tr>
              <th
                class="w3-mobile w3-text-grey w3-small"
                scope="row"
              >
                Account Number
              </th>
              <td class="reference-fact w3-section w3-mobile">
                <span class="reference-value w3-block">
                  {{ detail.fullAccountNumber || 'Number unavailable' }}
                </span>
                <CopyReference
                  v-if="detail.fullAccountNumber"
                  :reference="detail.fullAccountNumber"
                  label="Copy Account Number"
                />
              </td>
            </tr>

            <tr>
              <th
                class="w3-mobile w3-text-grey w3-small"
                scope="row"
              >
                Customer Registered
              </th>
              <td class="w3-mobile">{{ formatDate(detail.customer.registeredAt) }}</td>
            </tr>
          </tbody>
        </table>
      </div>
    </DisclosurePanel>
    <h2 class="w3-sans-serif w3-large">Transaction</h2>
    <div class="w3-responsive">
      <table class="assistance-facts w3-container w3-table w3-bordered">
        <tbody>
          <tr>
            <th
              class="w3-mobile w3-text-grey w3-small"
              scope="row"
            >
              Merchant
            </th>
            <td class="w3-mobile">{{ detail.transaction.merchantName }}</td>
          </tr>

          <tr>
            <th
              class="w3-mobile w3-text-grey w3-small"
              scope="row"
            >
              Amount
            </th>
            <td class="w3-mobile">
              {{ formatMoney(detail.transaction.amount, detail.transaction.currency) }} ·
              {{ detail.transaction.direction }}
            </td>
          </tr>

          <tr>
            <th
              class="w3-mobile w3-text-grey w3-small"
              scope="row"
            >
              Date &amp; Time
            </th>
            <td class="w3-mobile">{{ formatDate(detail.transaction.transactionAt) }}</td>
          </tr>

          <tr>
            <th
              class="w3-mobile w3-text-grey w3-small"
              scope="row"
            >
              Type &amp; Status
            </th>
            <td class="w3-mobile">
              {{ detail.transaction.type.replace(/([a-z])([A-Z])/g, '$1 $2') }} ·
              {{ detail.transaction.status }}
            </td>
          </tr>

          <tr>
            <th
              class="w3-mobile w3-text-grey w3-small"
              scope="row"
            >
              Description
            </th>
            <td class="w3-mobile">{{ detail.transaction.description }}</td>
          </tr>

          <tr class="full-fact">
            <th
              class="w3-mobile w3-text-grey w3-small"
              scope="row"
            >
              Bank Reference
            </th>
            <td class="reference-fact w3-section w3-mobile">
              <span class="reference-value w3-block">{{ detail.transaction.reference }}</span>
              <CopyReference
                :reference="detail.transaction.reference"
                label="Copy Transaction Reference"
              />
            </td>
          </tr>

          <tr
            v-if="detail.transaction.refundReference"
            class="full-fact"
          >
            <th
              class="w3-mobile w3-text-grey w3-small"
              scope="row"
            >
              Customer Refund Reference
            </th>
            <td class="reference-fact w3-section w3-mobile">
              <span class="reference-value w3-block">
                {{ detail.transaction.refundReference }}
              </span>
              <CopyReference
                :reference="detail.transaction.refundReference"
                label="Copy Refund Reference"
              />
            </td>
          </tr>

          <tr class="full-fact">
            <th
              class="w3-mobile w3-text-grey w3-small"
              scope="row"
            >
              Transaction ID
            </th>
            <td class="reference-fact w3-section w3-mobile">
              <span class="reference-value w3-block">{{ detail.transaction.id }}</span>
              <CopyReference
                :reference="detail.transaction.id"
                label="Copy Internal Transaction ID"
              />
            </td>
          </tr>
        </tbody>
      </table>
    </div>
  </section>
</template>
