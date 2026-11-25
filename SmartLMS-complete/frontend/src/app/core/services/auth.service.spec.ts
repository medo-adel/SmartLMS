import { describe, it, expect } from 'vitest';
import { dashboardFor } from './auth.service';

describe('dashboardFor', () => {
  it('maps roles to the right dashboard, highest privilege first', () => {
    expect(dashboardFor(['Student'])).toBe('student');
    expect(dashboardFor(['Parent'])).toBe('parent');
    expect(dashboardFor(['Instructor'])).toBe('instructor');
    // API: instructor dashboard is Instructor-only, admin dashboard is TenantAdmin-only
    expect(dashboardFor(['TeachingAssistant'])).toBe('none');
    expect(dashboardFor(['SuperAdmin'])).toBe('none');
    expect(dashboardFor(['TenantAdmin'])).toBe('admin');
    expect(dashboardFor(['Student', 'TenantAdmin'])).toBe('admin');
    expect(dashboardFor([])).toBe('none');
  });
});
