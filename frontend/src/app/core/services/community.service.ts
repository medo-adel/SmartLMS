import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { firstValueFrom } from 'rxjs';
import { environment } from '../../../environments/environment';
import { ApiResponse } from '../models/api-response';
import { AnnouncementRequest, AnnouncementRow, ChatMessagesPage, ChatRoom, ChatSendRequest, LiveAttendanceRow, PathDetail, PathRequest, PathRow, LiveSessionRequest, LiveSessionRow, PostRequest, ThreadDetail, ThreadRequest, ThreadRow } from '../models/community';

/** Discussion threads, announcements (CommunityController). The API enforces roles: post/reply/upvote = Student/Instructor/TA, pin = +TenantAdmin, announcements = staff. */
@Injectable({ providedIn: 'root' })
export class CommunityService {
  private readonly http = inject(HttpClient);
  private readonly base = environment.apiBaseUrl;

  private async unwrap<T>(req: Promise<ApiResponse<T>>): Promise<T> {
    const res = await req;
    if (!res.success) throw new Error(res.message ?? 'request failed');
    return res.data as T;
  }
  private get<T>(path: string, params?: HttpParams) { return this.unwrap(firstValueFrom(this.http.get<ApiResponse<T>>(`${this.base}${path}`, { params }))); }
  private post<T>(path: string, body: unknown = {}) { return this.unwrap(firstValueFrom(this.http.post<ApiResponse<T>>(`${this.base}${path}`, body))); }
  private id(v: string) { return encodeURIComponent(v); }

  threads(courseId: string, lessonId?: string) {
    let p = new HttpParams(); if (lessonId) p = p.set('lessonId', lessonId);
    return this.get<ThreadRow[]>(`/courses/${this.id(courseId)}/threads`, p);
  }
  thread(id: string) { return this.get<ThreadDetail>(`/threads/${this.id(id)}`); }
  createThread(courseId: string, r: ThreadRequest) { return this.post<{ id: string }>(`/courses/${this.id(courseId)}/threads`, r); }
  reply(threadId: string, r: PostRequest) { return this.post<{ id: string }>(`/threads/${this.id(threadId)}/posts`, r); }
  upvoteThread(id: string) { return this.post<{ upvoteCount?: number }>(`/threads/${this.id(id)}/upvote`); }
  upvotePost(id: string) { return this.post<{ upvoteCount?: number }>(`/posts/${this.id(id)}/upvote`); }
  pinThread(id: string) { return this.post<unknown>(`/threads/${this.id(id)}/pin`); }

  announcements() { return this.get<AnnouncementRow[]>('/announcements'); }

  // ---- chat (all signed-in roles; backend scopes rooms to the caller's courses) ----
  rooms() { return this.get<ChatRoom[]>('/chat/rooms'); }
  messages(roomId: string, before?: string | null) {
    let p = new HttpParams(); if (before) p = p.set('before', before);
    return this.get<ChatMessagesPage>(`/chat/rooms/${this.id(roomId)}/messages`, p);
  }
  sendMessage(roomId: string, r: ChatSendRequest) { return this.post<{ id: string }>(`/chat/rooms/${this.id(roomId)}/messages`, r); }

  // ---- learning paths: list/detail public (published), create/update = Mgr, delete = TenantAdmin, enroll = Student ----
  paths() { return this.get<PathRow[]>('/learning-paths'); }
  path(id: string) { return this.get<PathDetail>(`/learning-paths/${this.id(id)}`); }
  createPath(r: PathRequest) { return this.post<{ id: string }>('/learning-paths', r); }
  updatePath(id: string, r: PathRequest) { return this.unwrap(firstValueFrom(this.http.put<ApiResponse<unknown>>(`${this.base}/learning-paths/${this.id(id)}`, r))); }
  deletePath(id: string) { return this.unwrap(firstValueFrom(this.http.delete<ApiResponse<unknown>>(`${this.base}/learning-paths/${this.id(id)}`))); }
  enrollPath(id: string) { return this.post<{ id: string }>(`/learning-paths/${this.id(id)}/enroll`); }

  // ---- live sessions: create = Instructor, join = Student, attendance = staff ----
  liveSessions(courseId: string) { return this.get<LiveSessionRow[]>(`/courses/${this.id(courseId)}/live-sessions`); }
  createLiveSession(courseId: string, r: LiveSessionRequest) { return this.post<{ id: string }>(`/courses/${this.id(courseId)}/live-sessions`, r); }
  joinLive(id: string) { return this.post<{ joinUrl: string }>(`/live-sessions/${this.id(id)}/join`); }
  liveAttendance(id: string) { return this.get<LiveAttendanceRow[]>(`/live-sessions/${this.id(id)}/attendance`); }
  announce(r: AnnouncementRequest) { return this.post<{ id: string }>('/announcements', r); }
}
