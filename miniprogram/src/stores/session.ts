import { reactive, computed } from 'vue';
import { getStorage, removeStorage, setStorage } from '../utils/storage';

const USERNAME_KEY = 'fund_username';
const AVATAR_KEY = 'fund_avatar';
const SESSION_KEY = 'fund_session';

export interface SessionState {
  username: string;
  displayName: string;
  avatarDataUrl: string;
  avatarUrl: string;
  loginTime: number;
}

export const sessionState = reactive<SessionState>({
  username: '',
  displayName: '',
  avatarDataUrl: '',
  avatarUrl: '',
  loginTime: 0
});

export interface SessionPayload {
  username: string;
  displayName?: string;
  avatarDataUrl?: string;
  avatarUrl?: string;
  loginTime?: number;
}

export const isLoggedInRef = computed(() => Boolean(sessionState.username));

function normalizeSession(value: Partial<SessionPayload> | null | undefined): SessionPayload | null {
  const username = String(value?.username || '').trim();
  if (!username) return null;

  return {
    username,
    displayName: String(value?.displayName || username),
    avatarDataUrl: String(value?.avatarDataUrl || ''),
    avatarUrl: String(value?.avatarUrl || ''),
    loginTime: Number(value?.loginTime || Date.now())
  };
}

function applySession(value: SessionPayload | null) {
  sessionState.username = value?.username || '';
  sessionState.displayName = value?.displayName || '';
  sessionState.avatarDataUrl = value?.avatarDataUrl || '';
  sessionState.avatarUrl = value?.avatarUrl || '';
  sessionState.loginTime = value?.loginTime || 0;
}

export function saveSession(payload: SessionPayload) {
  const next = normalizeSession({
    ...sessionState,
    ...payload,
    loginTime: payload.loginTime || sessionState.loginTime || Date.now()
  });
  applySession(next);

  if (!next) return null;

  setStorage(SESSION_KEY, next);
  setStorage(USERNAME_KEY, next.username);
  setStorage(AVATAR_KEY, next.avatarDataUrl || next.avatarUrl || '');
  return next;
}

export function loadSession() {
  const stored = normalizeSession(getStorage<Partial<SessionPayload> | null>(SESSION_KEY, null));
  if (stored) {
    applySession(stored);
    return stored;
  }

  const legacy = normalizeSession({
    username: getStorage(USERNAME_KEY, ''),
    avatarDataUrl: getStorage(AVATAR_KEY, ''),
    loginTime: Date.now()
  });
  applySession(legacy);
  if (legacy) setStorage(SESSION_KEY, legacy);
  return legacy;
}

export function restoreSession() {
  return loadSession();
}

export function isLoggedIn() {
  return Boolean(sessionState.username || loadSession()?.username);
}

export function setSession(username: string, avatarDataUrl = '', patch: Partial<SessionPayload> = {}) {
  return saveSession({
    ...patch,
    username,
    displayName: patch.displayName || sessionState.displayName || username,
    avatarDataUrl: avatarDataUrl || patch.avatarDataUrl || sessionState.avatarDataUrl,
    avatarUrl: patch.avatarUrl || sessionState.avatarUrl
  });
}

export function clearSession() {
  applySession(null);
  removeStorage(SESSION_KEY);
  removeStorage(USERNAME_KEY);
  removeStorage(AVATAR_KEY);
}
