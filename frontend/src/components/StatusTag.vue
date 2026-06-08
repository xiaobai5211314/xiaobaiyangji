<template>
  <span :class="['xb-tag', variantClass]" :title="tooltip">
    <slot />
  </span>
</template>

<script setup>
import { computed } from 'vue'

const props = defineProps({
  variant: { type: String, default: 'ghost' },
  tooltip: { type: String, default: '' }
})

const variantClass = computed(() => `xb-tag--${props.variant}`)
</script>
