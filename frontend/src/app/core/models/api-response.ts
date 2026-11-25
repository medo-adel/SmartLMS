/** Mirrors SmartLMS.Application.Common.ApiResponse<T> from the .NET API (camelCase JSON). */
export interface ApiResponse<T> {
  readonly success: boolean;
  readonly data: T | null;
  readonly message: string | null;
  readonly code?: string | null;
  readonly errors: readonly string[];
}
