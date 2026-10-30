import { useEffect, useMemo, useState } from "react";
import { Settings2 } from "lucide-react";
import { toast } from "sonner";
import { cn } from "@/lib/utils";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Textarea } from "@/components/ui/textarea";
import { useUpdateProjectPortal } from "../../hooks/use-build-portal";
import {
  DEFAULT_PORTAL_VISIBILITY,
  type BuildPortalAccess, type BuildPortalCompanyDto, type BuildProjectPortalDto, type UpdateBuildProjectPortalRequest,
} from "../../api/build-portal.api";
import { isHexColor, slugify, slugProblem, trimHyphens } from "../../lib/portal";
import { PortalImageUpload } from "./PortalImageUpload";
import { PortalVisibilityMatrix } from "./PortalVisibilityMatrix";
import { PortalPasswordControl } from "./PortalPasswordControl";
import { PortalPublishPanel } from "./PortalPublishPanel";

type Form = UpdateBuildProjectPortalRequest;

function toForm(p: BuildProjectPortalDto): Form {
  return {
    slug:            p.slug,
    access:          p.access ?? "Public",
    title:           p.title,
    welcomeMessage:  p.welcomeMessage,
    logoKey:         p.logoKey,
    coverKey:        p.coverKey,
    primaryColor:    p.primaryColor,
    accentColor:     p.accentColor,
    contactName:     p.contactName,
    contactPhone:    p.contactPhone,
    contactEmail:    p.contactEmail,
    contactWhatsApp: p.contactWhatsApp,
    visibility:      { ...DEFAULT_PORTAL_VISIBILITY, ...p.visibility },
  };
}

/** Empty strings → null and the slug without stray hyphens: what is compared and sent. */
function clean(f: Form): Form {
  const t = (v: string | null) => (v && v.trim() ? v.trim() : null);
  const slug = f.slug ? trimHyphens(f.slug) : "";
  return {
    ...f,
    slug:            slug || null,
    title:           t(f.title),
    welcomeMessage:  t(f.welcomeMessage),
    primaryColor:    t(f.primaryColor),
    accentColor:     t(f.accentColor),
    contactName:     t(f.contactName),
    contactPhone:    t(f.contactPhone),
    contactEmail:    t(f.contactEmail),
    contactWhatsApp: t(f.contactWhatsApp),
  };
}

interface Props {
  projectId: string;
  portal:    BuildProjectPortalDto;
  company:   BuildPortalCompanyDto | undefined;
}

export function ProjectPortalSettings({ projectId, portal, company }: Props) {
  const mut = useUpdateProjectPortal(projectId);
  const [form, setForm] = useState<Form>(() => toForm(portal));
  const [coverPreview, setCoverPreview] = useState<string | null>(portal.coverUrl);

  useEffect(() => { setForm(toForm(portal)); setCoverPreview(portal.coverUrl); }, [portal]);

  const set = <K extends keyof Form>(k: K, v: Form[K]) => setForm((f) => ({ ...f, [k]: v }));

  const saved   = useMemo(() => JSON.stringify(clean(toForm(portal))), [portal]);
  const current = clean(form);
  const dirty   = !portal.exists || JSON.stringify(current) !== saved;

  const slugError  = current.slug ? slugProblem(current.slug) : null;
  const colorError = [current.primaryColor, current.accentColor].some((c) => c && !isHexColor(c))
    ? "Cor inválida: use o formato #RRGGBB." : null;

  const save = () => {
    if (slugError)  { toast.error(slugError); return; }
    if (colorError) { toast.error(colorError); return; }
    mut.mutate(current, {
      onSuccess: () => toast.success("Portal da obra salvo."),
      onError:   (e) => toast.error(e instanceof Error ? e.message : "Não foi possível salvar o portal."),
    });
  };

  const accessOptions: Array<{ value: BuildPortalAccess; label: string; hint: string }> = [
    { value: "Public",   label: "Público",   hint: "Quem tiver o link vê o portal." },
    { value: "Password", label: "Com senha", hint: "O cliente digita a senha para entrar." },
  ];

  return (
    <div className="space-y-4">
      <PortalPublishPanel projectId={projectId} portal={portal} company={company} dirty={dirty} />

      <section className="space-y-4 rounded-md border border-border bg-card p-4">
        <h3 className="flex items-center gap-1.5 text-sm font-semibold">
          <Settings2 className="h-4 w-4 text-muted-foreground" /> Portal desta obra
        </h3>

        <div className="grid gap-3 sm:grid-cols-2">
          <div className="space-y-1">
            <Label htmlFor="portal-slug" className="text-xs">Endereço da obra (slug)</Label>
            <Input id="portal-slug" value={form.slug ?? ""} maxLength={60} placeholder="residencial-parque-sul"
              onChange={(e) => set("slug", slugify(e.target.value))} className="text-sm" />
            {slugError && <p className="text-xs text-destructive">{slugError}</p>}
          </div>
          <div className="space-y-1">
            <Label htmlFor="portal-title" className="text-xs">Título</Label>
            <Input id="portal-title" value={form.title ?? ""} maxLength={200} placeholder="Residencial Parque Sul"
              onChange={(e) => set("title", e.target.value)} className="text-sm" />
          </div>
        </div>

        <div className="space-y-1">
          <Label htmlFor="portal-welcome" className="text-xs">Mensagem de boas-vindas</Label>
          <Textarea id="portal-welcome" value={form.welcomeMessage ?? ""} maxLength={2000}
            onChange={(e) => set("welcomeMessage", e.target.value)} className="min-h-[70px] resize-none text-sm"
            placeholder="Olá! Aqui você acompanha a evolução da sua obra." />
        </div>

        <div className="grid gap-3 sm:grid-cols-2">
          {(["primaryColor", "accentColor"] as const).map((k) => {
            const value = form[k] ?? "";
            const id = `portal-${k}`;
            return (
              <div key={k} className="space-y-1">
                <Label htmlFor={id} className="text-xs">{k === "primaryColor" ? "Cor principal" : "Cor de destaque"}</Label>
                <div className="flex items-center gap-2">
                  <input type="color" aria-label={`Escolher ${k === "primaryColor" ? "cor principal" : "cor de destaque"}`}
                    value={isHexColor(value) ? value : "#000000"}
                    onChange={(e) => set(k, e.target.value)}
                    className="h-9 w-10 cursor-pointer rounded border border-input bg-background p-0.5" />
                  <Input id={id} value={value} maxLength={7} placeholder="#RRGGBB"
                    onChange={(e) => set(k, e.target.value)} className={cn("text-sm", value && !isHexColor(value) && "border-destructive")} />
                </div>
              </div>
            );
          })}
        </div>
        {colorError && <p className="text-xs text-destructive">{colorError}</p>}

        <div className="space-y-1">
          <Label className="text-xs">Capa</Label>
          <PortalImageUpload context="build-portal-cover" label="Capa" wide previewUrl={coverPreview} hasImage={!!form.coverKey}
            onChange={(key, url) => { set("coverKey", key); setCoverPreview(url); }} />
        </div>

        <div className="grid gap-3 sm:grid-cols-2">
          {([
            ["contactName", "Responsável pelo contato", 200],
            ["contactPhone", "Telefone", 40],
            ["contactEmail", "E-mail", 200],
            ["contactWhatsApp", "WhatsApp", 40],
          ] as const).map(([k, label, max]) => (
            <div key={k} className="space-y-1">
              <Label htmlFor={`portal-${k}`} className="text-xs">{label}</Label>
              <Input id={`portal-${k}`} value={form[k] ?? ""} maxLength={max} type={k === "contactEmail" ? "email" : "text"}
                onChange={(e) => set(k, e.target.value)} className="text-sm" />
            </div>
          ))}
        </div>

        <fieldset className="space-y-2">
          <legend className="text-xs font-medium">Acesso</legend>
          <div role="radiogroup" aria-label="Acesso ao portal" className="grid gap-2 sm:grid-cols-2">
            {accessOptions.map((o) => (
              <button key={o.value} type="button" role="radio" aria-checked={form.access === o.value}
                onClick={() => set("access", o.value)}
                className={cn("rounded-md border p-2.5 text-left transition-colors",
                  form.access === o.value ? "border-primary bg-primary/5" : "border-border hover:bg-muted/40")}>
                <span className="block text-sm font-medium">{o.label}</span>
                <span className="block text-[11px] text-muted-foreground">{o.hint}</span>
              </button>
            ))}
          </div>
          {form.access === "Password" && !portal.hasPassword && (
            <p className="text-xs text-amber-700 dark:text-amber-400">Defina a senha abaixo antes de publicar com acesso por senha.</p>
          )}
          <PortalPasswordControl projectId={projectId} hasPassword={portal.hasPassword} isLocked={portal.isLocked} />
        </fieldset>

        <div className="space-y-2">
          <div>
            <h4 className="text-sm font-semibold">O que o cliente vê</h4>
            <p className="text-xs text-muted-foreground">Itens desligados nunca saem do sistema. A nota interna do diário nunca é mostrada.</p>
          </div>
          <PortalVisibilityMatrix value={form.visibility} onChange={(v) => set("visibility", v)} />
        </div>

        <div className="flex items-center justify-end gap-2">
          {dirty && portal.exists && <span className="text-xs text-muted-foreground">Alterações não salvas</span>}
          <Button size="sm" variant="ghost" disabled={!dirty || !portal.exists} onClick={() => { setForm(toForm(portal)); setCoverPreview(portal.coverUrl); }}>
            Descartar
          </Button>
          <Button size="sm" onClick={save} disabled={mut.isPending || !!slugError || !!colorError}>
            {mut.isPending ? "Salvando…" : "Salvar portal"}
          </Button>
        </div>
      </section>
    </div>
  );
}
