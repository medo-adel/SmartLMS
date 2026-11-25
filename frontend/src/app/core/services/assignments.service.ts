import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { firstValueFrom } from 'rxjs';
import { environment } from '../../../environments/environment';
import { ApiResponse } from '../models/api-response';
import { AssignmentRequest, AssignmentRow, GradeSubmissionRequest, GroupRequest, PeerReviewItem, ReturnRequest, ReviewRequest, SubmissionDetail, SubmissionRow, SubmitRequest } from '../models/assignments';

/** Assignment endpoints (AssignmentsController). The API enforces roles: manage = Instructor/TenantAdmin, grade = +TeachingAssistant, submit/review = Student. */
@Injectable({ providedIn: 'root' })
export class AssignmentsService {
  private readonly http = inject(HttpClient);
  private readonly base = environment.apiBaseUrl;

  private async unwrap<T>(req: Promise<ApiResponse<T>>): Promise<T> {
    const res = await req;
    if (!res.success) throw new Error(res.message ?? 'request failed');
    return res.data as T;
  }
  private get<T>(path: string) { return this.unwrap(firstValueFrom(this.http.get<ApiResponse<T>>(`${this.base}${path}`))); }
  private post<T>(path: string, body: unknown = {}) { return this.unwrap(firstValueFrom(this.http.post<ApiResponse<T>>(`${this.base}${path}`, body))); }
  private put<T>(path: string, body: unknown) { return this.unwrap(firstValueFrom(this.http.put<ApiResponse<T>>(`${this.base}${path}`, body))); }
  private id(v: string) { return encodeURIComponent(v); }

  list(courseId: string) { return this.get<AssignmentRow[]>(`/courses/${this.id(courseId)}/assignments`); }
  create(courseId: string, r: AssignmentRequest) { return this.post<{ id: string }>(`/courses/${this.id(courseId)}/assignments`, r); }
  update(id: string, r: AssignmentRequest) { return this.put<unknown>(`/assignments/${this.id(id)}`, r); }

  submissions(assignmentId: string) { return this.get<SubmissionRow[]>(`/assignments/${this.id(assignmentId)}/submissions`); }
  grade(submissionId: string, r: GradeSubmissionRequest) { return this.put<unknown>(`/submissions/${this.id(submissionId)}/grade`, r); }
  returnForResubmission(submissionId: string, r: ReturnRequest) { return this.post<unknown>(`/submissions/${this.id(submissionId)}/return`, r); }
  comment(submissionId: string, body: string, anchorJson: string | null = null) { return this.post<{ id: string }>(`/submissions/${this.id(submissionId)}/comments`, { body, anchorJson }); }

  assignPeerReviews(assignmentId: string) { return this.post<unknown>(`/assignments/${this.id(assignmentId)}/peer-reviews/assign`); }

  // student side
  group(assignmentId: string, r: GroupRequest) { return this.post<unknown>(`/assignments/${this.id(assignmentId)}/groups`, r); }
  submit(assignmentId: string, r: SubmitRequest) { return this.post<unknown>(`/assignments/${this.id(assignmentId)}/submissions`, r); }
  submission(id: string) { return this.get<SubmissionDetail>(`/submissions/${this.id(id)}`); }
  myReviews() { return this.get<PeerReviewItem[]>('/peer-reviews/mine'); }
  review(id: string, r: ReviewRequest) { return this.put<unknown>(`/peer-reviews/${this.id(id)}`, r); }
}
