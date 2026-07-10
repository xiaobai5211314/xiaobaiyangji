<script lang="ts">
import { restoreSession } from './stores/session';
import { loadTheme } from './stores/theme';

export default {
  onLaunch() {
    loadTheme();
    restoreSession();
  }
};
</script>

<style lang="scss">
@import './styles/variables.scss';
@import './styles/mixins.scss';
@import './styles/theme.scss';

page {
  min-height: 100%;
  @include page-gradient;
  color: $text-primary;
  font-family: "Microsoft YaHei", "微软雅黑", "PingFang SC", "Helvetica Neue", Arial, sans-serif;
}

.page {
  min-height: 100vh;
  box-sizing: border-box;
  color: var(--text-primary, #f5f5f7);
}

button {
  border: 0;
  font-family: "Microsoft YaHei", "微软雅黑", "PingFang SC", "Helvetica Neue", Arial, sans-serif;
}

button::after {
  border: 0;
}

input {
  color: var(--input-text, var(--text-primary, #f5f5f7));
  font-family: "Microsoft YaHei", "微软雅黑", "PingFang SC", "Helvetica Neue", Arial, sans-serif;
}

view,
text,
textarea {
  font-family: "Microsoft YaHei", "微软雅黑", "PingFang SC", "Helvetica Neue", Arial, sans-serif;
}
</style>
