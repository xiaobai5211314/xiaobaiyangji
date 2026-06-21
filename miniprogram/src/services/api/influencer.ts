import { get } from '../request';

export type TranslationStatus = 'success' | 'failed' | 'skipped';

export interface InfluencerPost {
  id?: string;
  externalId?: string;
  authorName?: string;
  authorHandle?: string;
  text: string;
  translatedText?: string;
  translatedAt?: string;
  translationProvider?: string;
  translationStatus?: TranslationStatus;
  createdAt: string;
  url: string;
  likeCount?: number;
  retweetCount?: number;
  replyCount?: number;
  quoteCount?: number;
  replies?: InfluencerReply[];
  mediaUrls?: string[];
  source?: string;
  _showReplies?: boolean;
}

export interface InfluencerPostsResponse {
  success?: boolean;
  status?: string;
  fetchedAt?: string;
  items?: InfluencerPost[];
}

export interface InfluencerReply {
  id?: string;
  text: string;
  translatedText?: string;
  translatedAt?: string;
  translationProvider?: string;
  translationStatus?: TranslationStatus;
  createdAt?: string;
  authorName?: string;
  authorUsername?: string;
  likeCount?: number;
  url?: string;
}

export function getInfluencerPosts(force = false) {
  const query = new URLSearchParams({ limit: '20' });
  if (force) query.set('_t', String(Date.now()));
  return get<InfluencerPostsResponse>(`/api/influencer-posts/latest?${query}`, {
    silent: true,
    showErrorToast: false,
    fallbackData: { success: false, status: 'unavailable', items: [] }
  });
}
