export interface AppEnvironment {
  readonly production: boolean;
  readonly apiBaseUrl: string;
  readonly defaultTenantId: string;
}

export const environment: AppEnvironment = {
  production: false,
  apiBaseUrl: 'https://localhost:7200/api/v1',
  defaultTenantId: 'demo',
};
