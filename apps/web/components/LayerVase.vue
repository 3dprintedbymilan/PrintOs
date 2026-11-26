<script setup lang="ts">
const layers = [
  { width: 26, y: 4 },
  { width: 24, y: 10 },
  { width: 22, y: 16 },
  { width: 24, y: 22 },
  { width: 30, y: 28 },
  { width: 38, y: 34 },
  { width: 46, y: 40 },
  { width: 52, y: 46 },
  { width: 54, y: 52 },
  { width: 52, y: 58 },
  { width: 46, y: 64 },
  { width: 38, y: 70 },
  { width: 30, y: 76 },
  { width: 34, y: 82 },
]
</script>

<template>
  <svg
    class="vase"
    viewBox="0 0 64 88"
    width="64"
    height="88"
    aria-hidden="true"
    focusable="false"
  >
    <line
      v-for="(layer, index) in layers"
      :key="layer.y"
      class="layer"
      :x1="32 - layer.width / 2"
      :x2="32 + layer.width / 2"
      :y1="layer.y"
      :y2="layer.y"
      :style="{ '--delay': `${(layers.length - 1 - index) * 55}ms` }"
    />
  </svg>
</template>

<style scoped>
.vase { flex: none; color: var(--accent); }
.layer {
  stroke: currentColor;
  stroke-width: 3.2;
  stroke-linecap: round;
  transform-origin: 32px 0;
  transform-box: view-box;
  opacity: 0.85;
  animation: print-layer 520ms cubic-bezier(0.16, 1, 0.3, 1) both;
  animation-delay: var(--delay);
}
@keyframes print-layer {
  from { opacity: 0; transform: scaleX(0.2); }
  to { opacity: 0.85; transform: scaleX(1); }
}
@media (prefers-reduced-motion: reduce) {
  .layer { animation: none; }
}
</style>
