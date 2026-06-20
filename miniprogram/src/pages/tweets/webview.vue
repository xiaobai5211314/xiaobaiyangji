<template>
  <web-view v-if="sourceUrl" :src="sourceUrl" />
</template>

<script setup lang="ts">
import { ref } from 'vue';
import { onLoad } from '@dcloudio/uni-app';

const sourceUrl = ref('');

onLoad((options) => {
  const candidate = decodeURIComponent(String(options?.url || ''));
  if (/^https:\/\/(x\.com|twitter\.com)\//i.test(candidate)) {
    sourceUrl.value = candidate;
  }
});
</script>
