<script setup lang="ts">
import { computed } from 'vue'
import type { Settings } from '../platform'
const props = defineProps<{ before?: Settings | null; after: Settings; changesOnly?: boolean }>()
function flatten(value: unknown, prefix = '', result: Record<string, string> = {}) {
  if (value !== null && typeof value === 'object') {
    for (const [key, item] of Object.entries(value)) {
      if (!prefix && (key === 'publication' || key === 'version')) continue
      flatten(item, prefix ? `${prefix}.${key}` : key, result)
    }
  } else
    result[prefix] =
      value == null
        ? 'Not Set'
        : typeof value === 'boolean'
          ? value
            ? 'Yes'
            : 'No'
          : String(value)
  return result
}
const rows = computed(() => {
  const before = flatten(props.before),
    after = flatten(props.after)
  return [...new Set([...Object.keys(before), ...Object.keys(after)])].sort().flatMap((key) => {
    const previous = before[key] ?? 'Not Set',
      next = after[key] ?? 'Not Set'
    if (props.changesOnly && previous === next) return []
    return [
      { key, label: key.replace(/([a-z])([A-Z])/g, '$1 $2').replaceAll('.', ' · '), previous, next }
    ]
  })
})
</script>
<template>
  <div class="settings-comparison w3-section">
    <p v-if="!rows.length">No Business Values Change. Publication Still Creates A New Version.</p>
    <table
      v-else
      class="w3-table w3-striped w3-bordered"
    >
      <thead>
        <tr>
          <th>Setting</th>
          <th v-if="before">Before</th>
          <th>{{ before ? 'After' : 'Retained Value' }}</th>
        </tr>
      </thead>
      <tbody>
        <tr
          v-for="row in rows"
          :key="row.key"
        >
          <th>{{ row.label }}</th>
          <td v-if="before">
            {{ row.previous }}
          </td>
          <td>{{ row.next }}</td>
        </tr>
      </tbody>
    </table>
  </div>
</template>
