/** Assignments contracts, mirrored from the delivered backend (AssignmentService.cs records + AssignmentsController). */

/** POST/PUT body for creating or updating an assignment (AssignmentRequest). */
export interface AssignmentRequest {
  title: string;
  instructions: string | null;
  dueAtUtc: string | null;
  maxPoints: number | null;
  allowResubmission: boolean;
  maxResubmissions: number | null;
  isGroup: boolean;
  maxGroupSize: number | null;
  peerReviewEnabled: boolean;
  peerReviewsPerSubmission: number | null;
  isPublished: boolean;
  rubric: RubricItem[] | null;
  plagiarismCheckEnabled?: boolean | null;
  aiProvisionalGradingEnabled?: boolean | null;
}
export interface RubricItem { name: string; description: string | null; maxPoints: number; }

/** Assignment row returned by GET /courses/{courseId}/assignments (AssignmentService.ListAsync). */
export interface AssignmentRow {
  id: string;
  title: string;
  instructions: string | null;
  dueAtUtc: string | null;
  maxPoints: number | null;
  isGroup: boolean;
  maxGroupSize: number | null;
  allowResubmission: boolean;
  peerReviewEnabled: boolean;
  isPublished: boolean;
  rubric: { id: string; name: string; description: string | null; maxPoints: number }[];
  /** The caller's newest submission (own or team), added to the list payload; null when never submitted. Absent on older backends. */
  mine?: { id: string; version: number; status: string; totalScore: number | null; isLate: boolean; submittedAtUtc: string } | null;
}

/** Submission row returned by GET /assignments/{id}/submissions (AssignmentService.SubmissionsAsync). */
export interface SubmissionRow {
  id: string;
  userId: string;
  assignmentGroupId: string | null;
  version: number;
  isLate: boolean;
  status: string;
  totalScore: number | null;
  submittedAtUtc: string;
  studentName: string;
}

export interface ScoreItem { criterionId: string; points: number; feedback: string | null; }

/** GET /submissions/{id} (AssignmentService.SubmissionAsync). Staff see author/status/scores; peer reviewers get content only. */
export interface SubmissionDetail {
  id: string;
  version: number;
  textContent: string | null;
  linkUrl: string | null;
  isLate: boolean;
  submittedAtUtc: string;
  status: string | null;
  totalScore: number | null;
  feedback: string | null;
  author: string | null;
  comments: { id: string; body: string; anchorJson: string | null; createdAtUtc: string }[];
  scores: { criterionId: string; points: number; feedback: string | null }[];
  maxPoints: number | null;
}

/** GET /peer-reviews/mine row (AssignmentService.MyReviewsAsync). */
export interface PeerReviewItem {
  id: string;
  submissionId: string;
  status: string;
  assignmentTitle: string;
  maxPoints: number | null;
  rubric: { id: string; name: string; maxPoints: number }[];
}
export interface GradeSubmissionRequest { scores: ScoreItem[] | null; totalScore: number | null; feedback: string | null; }
export interface ReturnRequest { feedback: string | null; }
export interface ReviewRequest { scores: ScoreItem[]; comments: string | null; }
export interface GroupRequest { groupId: string | null; name: string | null; }
export interface SubmitRequest { textContent: string | null; linkUrl: string | null; }
