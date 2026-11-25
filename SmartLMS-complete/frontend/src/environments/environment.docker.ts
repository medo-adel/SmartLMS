/** Used by the Docker image only (replaces environment.ts). nginx serves the app and proxies /api and /hubs to the API container, so URLs are same-origin. */
export interface AppEnvironment {
  readonly production: boolean;
  readonly apiBaseUrl: string;
  readonly defaultTenantId: string;
}

export const environment: AppEnvironment = {
  production: true,
  apiBaseUrl: '/api/v1',
  defaultTenantId: 'demo',
};
