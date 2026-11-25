/** Community contracts, mirrored from the delivered backend (CommunityController + CommunityService). */

export interface ThreadRow { id: string; title: string; lessonId: string | null; threadType: string; isPinned: boolean; isLocked: boolean; upvoteCount: number; createdAtUtc: string; authorName: string | null; }
export interface ThreadDetail { id: string; title: string; body: string; isPinned: boolean; isLocked: boolean; upvoteCount: number; createdAtUtc: string; posts: PostRow[]; }
export interface PostRow { id: string; parentPostId: string | null; body: string; isInstructorPinned: boolean; upvoteCount: number; createdAtUtc: string; authorName: string | null; }
export interface ThreadRequest { title: string; body: string; lessonId: string | null; }
export interface PostRequest { body: string; parentPostId: string | null; }
export interface AnnouncementRow { id: string; courseId: string | null; title: string; body: string; publishedAtUtc: string; }
export interface AnnouncementRequest { courseId: string | null; title: string; body: string; }
export interface ChatRoom { id: string; name: string; roomType: string; courseId: string | null; }
export interface ChatMessage { id: string; senderId: string; senderName: string | null; body: string; sentAtUtc: string; }
export interface ChatMessagesPage { messages: ChatMessage[]; nextBefore: string | null; }
export interface ChatSendRequest { body: string; }
export interface LiveSessionRow { id: string; title: string; provider: string; startsAtUtc: string; endsAtUtc: string | null; status: string; joinUrl: string | null; }
export interface LiveSessionRequest { title: string; provider: string; joinUrl: string; startsAtUtc: string; endsAtUtc: string | null; }
export interface LiveAttendanceRow { userId: string; fullName: string; joinedAtUtc: string; }
export interface PathRow { id: string; title: string; slug: string; description: string | null; }
export interface PathDetail { id: string; title: string; description: string | null; status: string; issuesCertificate: boolean; courses: { id: string; title: string; sortOrder: number; isMandatory: boolean }[]; }
export interface PathRequest { title: string; description: string | null; status: string | null; issuesCertificate: boolean; courseIds: string[] | null; }
