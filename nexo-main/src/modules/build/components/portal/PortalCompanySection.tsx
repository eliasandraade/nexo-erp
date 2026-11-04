import { useEffect, useState } from "react";
import { Building2, Info } from "lucide-react";
import { toast } from "sonner";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { usePortalCompany, useUpdatePortalCompany } from "../../hooks/use-build-portal";
import { PORTAL_PUBLIC_ORIGIN } from "../../api/build-portal.api";
import { slugify, slugProblem, trimHyphens } from "../../lib/portal";
import { PortalImageUpload } from "./PortalImageUpload";

/** The construtora's portal identity: one slug shared by every obra. */
export function PortalCompanySection() {
  const { data: company, isLoading } = usePortalCompany();
  const mut = useUpdatePortalCompany();

  const [slug, setSlug]               = useState("");
  const [displayName, setDisplayName] = useState("");
  const [email, setEmail]             = useState("");
  const [phone, setPhone]             = useState("");
  const [logoKey, setLogoKey]         = useState<string | null>(null);
  const [logoPreview, setLogoPreview] = useState<string | null>(null);

  useEffect(() => {
    if (!company) return;
    setSlug(company.slug ?? "");
    setDisplayName(company.displayName ?? "");
    setEmail(company.contactEmail ?? "");
    setPhone(company.contactPhone ?? "");
    setLogoKey(company.logoKey);
    setLogoPreview(company.logoUrl);
  }, [company]);

  const cleanSlug = trimHyphens(slug);
  const problem = slugProblem(cleanSlug);

  const save = () => {
    if (problem) { toast.error(problem); return; }
    if (!displayName.trim()) { toast.error("Informe o nome da empresa no portal."); return; }
    mut.mutate({
      slug:         cleanSlug,
      displayName:  displayName.trim(),
      logoKey,
      contactEmail: email.trim() || null,
      contactPhone: phone.trim() || null,
    }, {
      onSuccess: () => toast.success("Dados da empresa no portal salvos."),
      onError:   (e) => toast.error(e instanceof Error ? e.message : "Não foi possível salvar."),
    });
  };

  if (isLoading) return <div className="h-40 rounded-md bg-muted animate-pulse" />;

  return (
    <section className="space-y-3 rounded-md border border-border bg-card p-4">
      <div>
        <h3 className="flex items-center gap-1.5 text-sm font-semibold">
          <Building2 className="h-4 w-4 text-muted-foreground" /> Empresa no portal
        </h3>
        <p className="flex items-start gap-1 text-xs text-muted-foreground">
          <Info className="mt-0.5 h-3 w-3 shrink-0" />
          O endereço da empresa é compartilhado por todas as obras: cada portal fica em
          {" "}{PORTAL_PUBLIC_ORIGIN}/<span className="font-medium">{cleanSlug || "{empresa}"}</span>/{"{obra}"}.
          Mudá-lo muda o link de todas as obras publicadas.
        </p>
      </div>

      <div className="grid gap-3 sm:grid-cols-2">
        <div className="space-y-1">
          <Label htmlFor="portal-company-slug" className="text-xs">Endereço da empresa (slug) *</Label>
          <Input id="portal-company-slug" value={slug} maxLength={60} placeholder="construtora-silva"
            onChange={(e) => setSlug(slugify(e.target.value))} className="text-sm" />
          {slug && problem && <p className="text-xs text-destructive">{problem}</p>}
        </div>
        <div className="space-y-1">
          <Label htmlFor="portal-company-name" className="text-xs">Nome exibido *</Label>
          <Input id="portal-company-name" value={displayName} maxLength={200} placeholder="Construtora Silva"
            onChange={(e) => setDisplayName(e.target.value)} className="text-sm" />
        </div>
        <div className="space-y-1">
          <Label htmlFor="portal-company-email" className="text-xs">E-mail de contato</Label>
          <Input id="portal-company-email" type="email" value={email} maxLength={200}
            onChange={(e) => setEmail(e.target.value)} className="text-sm" />
        </div>
        <div className="space-y-1">
          <Label htmlFor="portal-company-phone" className="text-xs">Telefone de contato</Label>
          <Input id="portal-company-phone" value={phone} maxLength={40}
            onChange={(e) => setPhone(e.target.value)} className="text-sm" />
        </div>
      </div>

      <div className="space-y-1">
        <Label className="text-xs">Logo da empresa</Label>
        <PortalImageUpload context="build-portal-logo" label="Logo" previewUrl={logoPreview} hasImage={!!logoKey}
          onChange={(key, url) => { setLogoKey(key); setLogoPreview(url); }} />
      </div>

      <div className="flex justify-end">
        <Button size="sm" onClick={save} disabled={mut.isPending || !!problem || !displayName.trim()}>
          {mut.isPending ? "Salvando…" : company?.configured ? "Salvar empresa" : "Configurar empresa"}
        </Button>
      </div>
    </section>
  );
}
