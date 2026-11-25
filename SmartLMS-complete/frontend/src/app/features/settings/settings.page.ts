import { Component, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { IonContent, IonButton, IonSpinner } from '@ionic/angular';
import { AdminService } from '../../core/services/admin.service';
import { TenantService } from '../../core/services/tenant.service';
import { CategoryItem, FeatureFlag } from '../../core/models/models';
import { TranslatePipe } from '../../core/pipes/translate.pipe';

/** TenantAdmin: academy identity (name, colours, language, timezone, currency), logo, feature flags and course categories. */
@Component({ selector: 'app-settings', imports: [FormsModule, IonContent, IonButton, IonSpinner, TranslatePipe], templateUrl: './settings.page.html', styleUrl: './settings.page.scss' })
export class SettingsPage implements OnInit {
  private readonly api = inject(AdminService);
  readonly tenant = inject(TenantService);
  readonly flags = signal<FeatureFlag[] | null>(null);
  readonly cats = signal<CategoryItem[] | null>(null);
  readonly info = signal('');
  readonly failed = signal(false);
  readonly editingCat = signal('');

  name = ''; primaryColor = ''; secondaryColor = ''; accentColor = ''; defaultLanguage = 'ar';
  // No GET endpoint exposes the saved timezone/currency to a TenantAdmin, so these start empty and must be
  // typed explicitly. The placeholders show the platform defaults; we never guess persisted state.
  timeZoneId = ''; currency = '';
  logoFile: File | null = null;
  catName = ''; catParent = ''; catOrder = 0; catIcon = '';
  catEdit: Record<string, unknown> = {};

  ngOnInit(): void {
    const b = this.tenant.branding();
    if (b) {
      this.name = b.name; this.primaryColor = b.primaryColor ?? ''; this.secondaryColor = b.secondaryColor ?? ''; this.accentColor = b.accentColor ?? '';
      this.defaultLanguage = b.defaultLanguage || 'ar';
    }
    void this.load();
  }
  async load(): Promise<void> {
    this.failed.set(false);
    try {
      const [f, c] = await Promise.all([this.api.featureFlags(), this.api.categories()]);
      this.flags.set(f); this.cats.set(c);
    } catch { this.failed.set(true); }
  }

  async saveIdentity(): Promise<void> {
    this.info.set('');
    if (!this.name.trim()) return;
    try {
      await this.api.updateSettings({
        name: this.name.trim(), primaryColor: this.primaryColor || null, secondaryColor: this.secondaryColor || null, accentColor: this.accentColor || null,
        defaultLanguage: this.defaultLanguage, timeZoneId: this.timeZoneId.trim(), currency: this.currency.trim(),
      });
      await this.tenant.loadBranding();
      this.info.set('saved');
    } catch { /* toast by interceptor */ }
  }

  pickLogo(e: Event): void { this.logoFile = (e.target as HTMLInputElement).files?.[0] ?? null; }
  async uploadLogo(): Promise<void> {
    this.info.set('');
    if (!this.logoFile) return;
    try { await this.api.uploadLogo(this.logoFile); this.logoFile = null; await this.tenant.loadBranding(); this.info.set('logo'); } catch { /* toast by interceptor */ }
  }

  async toggleFlag(f: FeatureFlag): Promise<void> {
    this.info.set('');
    try { await this.api.setFeatureFlag(f.key, !f.isEnabled); await this.load(); } catch { /* toast by interceptor */ }
  }

  async addCategory(): Promise<void> {
    this.info.set('');
    if (!this.catName.trim()) return;
    try {
      await this.api.createCategory({ name: this.catName.trim(), parentCategoryId: this.catParent || null, sortOrder: this.catOrder, iconName: this.catIcon.trim() || null });
      this.catName = ''; this.catParent = ''; this.catOrder = 0; this.catIcon = '';
      this.info.set('catAdded'); await this.load();
    } catch { /* toast by interceptor */ }
  }
  startCat(c: CategoryItem): void {
    this.editingCat.set(c.id);
    this.catEdit = { name: c.name, parentCategoryId: c.parentCategoryId ?? '', sortOrder: c.sortOrder, iconName: c.iconName ?? '' };
  }
  async saveCat(id: string): Promise<void> {
    this.info.set('');
    try {
      await this.api.updateCategory(id, {
        name: String(this.catEdit['name'] ?? '').trim(), parentCategoryId: String(this.catEdit['parentCategoryId'] ?? '') || null,
        sortOrder: Number(this.catEdit['sortOrder']) || 0, iconName: String(this.catEdit['iconName'] ?? '').trim() || null,
      });
      this.editingCat.set(''); this.info.set('catSaved'); await this.load();
    } catch { /* toast by interceptor */ }
  }
  readonly confirmDeleteCat = signal('');
  /** Two-tap confirmation before deleting a category. */
  async deleteCat(id: string): Promise<void> {
    if (this.confirmDeleteCat() !== id) { this.confirmDeleteCat.set(id); return; }
    this.confirmDeleteCat.set(''); this.info.set('');
    try { await this.api.deleteCategory(id); this.info.set('catDeleted'); await this.load(); } catch { /* toast by interceptor */ }
  }
}
