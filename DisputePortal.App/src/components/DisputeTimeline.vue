<script setup lang="ts">
import DisclosurePanel from './DisclosurePanel.vue'
import ActionActor from './ActionActor.vue'
import { statusReferences } from '../portalReference'
import { computed } from 'vue'
import { Check, Circle, MessageCircle } from '@lucide/vue'
import type { DisputeDetail } from '../types'
import { formatDate } from '../utils/format'

const props = defineProps<{ detail: DisputeDetail; compact?: boolean }>()
const customerNotes = computed(() =>
  props.detail.notes.filter((note) => note.visibility === 'Customer')
)
</script>

<template>
  <section class="timeline-section">
    <h2 class="w3-sans-serif w3-large">{{ compact ? 'Case Activity' : 'Progress So Far' }}</h2>
    <ol class="timeline w3-ul w3-section">
      <li
        v-for="(event, index) in detail.history"
        :key="event.changedAt + '-' + index"
        class="timeline-item w3-padding-16 w3-border-bottom"
      >
        <span
          class="timeline-marker"
          :class="{ 'timeline-complete': index < detail.history.length - 1 }"
        >
          <Check
            v-if="index < detail.history.length - 1"
            :size="13"
          />
          <Circle
            v-else
            :size="10"
          />
        </span>
        <div class="timeline-content">
          <div class="timeline-head w3-section">
            <b>{{ statusReferences[event.newStatus]?.customerLabel ?? event.newStatus }}</b>
            <time>
              {{ formatDate(event.changedAt, { day: 'numeric', month: 'short', year: 'numeric' }) }}
            </time>
          </div>
          <p>{{ event.comment || 'Your dispute status was updated.' }}</p>
          <ActionActor
            :identity="event.actorIdentity"
            :fallback="event.actorName"
          />
        </div>
      </li>
    </ol>
    <DisclosurePanel
      v-if="customerNotes.length"
      class="customer-updates"
    >
      <template #heading>
        <MessageCircle
          class="w3-margin-right"
          :size="16"
        />
        Updates &amp; Replies
        <span>{{ customerNotes.length }}</span>
      </template>
      <article
        v-for="(note, index) in customerNotes"
        :key="note.createdAt + '-' + index"
        class="update-message w3-panel w3-light-grey w3-padding w3-round-large"
      >
        <p>{{ note.content }}</p>
        <ActionActor
          :identity="note.actorIdentity"
          :fallback="note.authorName"
        />
        <small class="w3-small">{{ formatDate(note.createdAt) }}</small>
      </article>
    </DisclosurePanel>
    <div
      v-if="detail.dispute.resolution && !compact"
      class="resolution-box w3-white w3-border w3-round-large w3-container w3-padding-large"
    >
      <p>{{ detail.dispute.resolution }}</p>
    </div>
  </section>
</template>
