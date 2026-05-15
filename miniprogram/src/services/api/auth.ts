import { getApiBaseUrl } from '../config';
import { get, postForm, postJson } from '../request';

export interface LoginRequest {
  username: string;
  password: string;
}

export interface LoginResponse {
  success: boolean;
  displayName?: string;
  username?: string;
  userName?: string;
  user?: {
    displayName?: string;
    username?: string;
    userName?: string;
    avatarDataUrl?: string;
    avatarUrl?: string;
  };
  avatarDataUrl?: string;
  avatarUrl?: string;
}

export interface WechatLoginRequest {
  code: string;
  nickname?: string;
  avatarDataUrl?: string;
}

export interface AvatarUploadResponse {
  success?: boolean;
  avatarDataUrl?: string;
  avatarUrl?: string;
  message?: string;
  msg?: string;
  title?: string;
  error?: string;
  [key: string]: unknown;
}

function toAuthForm(payload: LoginRequest): Record<string, string> {
  return {
    username: payload.username,
    password: payload.password
  };
}

export function login(payload: LoginRequest) {
  return postForm<LoginResponse>('/api/auth/login', toAuthForm(payload), {
    loadingText: '登录中'
  });
}

export function register(payload: LoginRequest) {
  return postForm<LoginResponse>('/api/auth/register', toAuthForm(payload), {
    loadingText: '注册中'
  });
}

export function wechatLogin(payload: WechatLoginRequest) {
  return postJson<LoginResponse, WechatLoginRequest>('/api/auth/wechat-login', payload, {
    loadingText: '微信登录中'
  });
}

export function getProfile(username: string) {
  return get<LoginResponse>(`/api/auth/profile?username=${encodeURIComponent(username)}`, {
    loadingText: '读取用户',
    fallbackData: { success: false, username }
  });
}

export function pickUsername(response: LoginResponse, fallback = '') {
  return response.user?.username || response.user?.userName || response.username || response.userName || fallback;
}

export function pickDisplayName(response: LoginResponse, fallback = '') {
  return response.user?.displayName || response.displayName || pickUsername(response, fallback);
}

export function pickAvatar(response: LoginResponse) {
  return response.user?.avatarDataUrl || response.user?.avatarUrl || response.avatarDataUrl || response.avatarUrl || '';
}

function parseUploadResponse(data: unknown): AvatarUploadResponse {
  if (typeof data !== 'string') return (data || {}) as AvatarUploadResponse;
  const text = data.trim();
  if (!text) return {};

  try {
    return JSON.parse(text) as AvatarUploadResponse;
  } catch {
    return { success: false, message: text };
  }
}

function uploadAvatarOnce(endpoint: string, username: string, filePath: string) {
  return new Promise<AvatarUploadResponse>((resolve, reject) => {
    uni.uploadFile({
      url: `${getApiBaseUrl()}${endpoint}`,
      filePath,
      name: 'avatarFile',
      formData: { username },
      success: (result) => {
        const statusCode = Number(result.statusCode || 0);
        const parsed = parseUploadResponse(result.data);
        if (statusCode < 200 || statusCode >= 300) {
          reject(new Error(parsed.message || parsed.msg || parsed.title || parsed.error || `头像上传失败：${statusCode}`));
          return;
        }

        resolve(parsed);
      },
      fail: reject
    });
  });
}

export async function uploadAvatar(username: string, filePath: string) {
  const endpoints = ['/api/auth/avatar-file-v3', '/api/auth/avatar-file-v2', '/api/auth/avatar-file'];
  let lastError: unknown = null;

  for (const endpoint of endpoints) {
    try {
      return await uploadAvatarOnce(endpoint, username, filePath);
    } catch (error) {
      lastError = error;
    }
  }

  throw lastError instanceof Error ? lastError : new Error('头像上传失败');
}
