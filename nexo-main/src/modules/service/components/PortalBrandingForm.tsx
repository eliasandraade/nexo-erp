import { useEffect, useState } from "react";
import { Check, Loader2 } from "lucide-react";
import { toast } from "sonner";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Textarea } from "@/components/ui/textarea";
import { ImageUploadButton } from "@/components/shared/ImageUploadButton";
import { useUpdatePortalBranding } from "../hooks/usePublicBookingSettings";
import type { PublicBookingSettingsDto } from "../api/service.api";

const HEX = /^#[0-9a-fA-F]{6}$/;

interface Props {
  settings: PublicBookingSettingsDto;
}

export function PortalBrandingForm({ settings }: Props) {
  const mut = useUpdatePortalBranding();

  const [displayName, setDisplayName]     = useState("");
  const [description, setDescription]     = useState("");
  const [logoUrl, setLogoUrl]             = useState<string | null>(null);
  const [coverImageUrl, setCoverImageUrl] = useState<string | null>(null);
  const [brandColor, setBrandColor]       = useState<string | null>(null);
  const [whatsApp, setWhatsApp]           = useState("");
  const [address, setAddress]             = useState("");

  useEffect(() => {
    setDisplayName(settings.displayName ?? "");
    setDescription(settings.description ?? "");
    setLogoUrl(settings.logoUrl ?? null);
    setCoverImageUrl(settings.coverImageUrl ?? null);
    setBrandColor(settings.brandColor ?? null);
    setWhatsApp(settings.whatsApp ?? "");
    setAddress(settings.address ?? "");
  }, [settings]);

  const colorValid = brandColor === null || HEX.test(brandColor);

  function save() {
    if (!colorValid) { toast.error("Cor da marca inválida."); return; }
    mut.mutate(
      {
        displayName:   displayName.trim() || null,
        description:   description.trim() || null,
        logoUrl,
        coverImageUrl,
        brandColor,
        whatsApp:      whatsApp.trim() || null,
        address:       address.trim() || null,
      },
      {
        onSuccess: () => toast.success("Identidade salva."),
        onError:   () => toast.error("Não foi possível salvar a identidade."),
      },
    );
  }

  return (
    <div className="space-y-5">
      <div className="grid gap-4 sm:grid-cols-2">
        <Field label="Logo" hint="Quadrado. JPG, PNG ou WebP.">
          <ImageUploadButton context="service-portal-logo" value={logoUrl} onChange={setLogoUrl} label="Logo" />
        </Field>
        <Field label="Banner / capa" hint="Imagem larga exibida no topo.">
          <ImageUploadButton context="service-portal-cover" value={coverImageUrl} onChange={setCoverImageUrl} label="Capa" />
        </Field>
      </div>

      <Field label="Nome de exibição" hint="Aparece no portal. Se vazio, usamos o nome da loja.">
        <Input value={displayName} onChange={(e) => setDisplayName(e.target.value)} placeholder="Ex: Clínica Vida" maxLength={120} />
      </Field>

      <Field label="Descrição pública" hint="Frase curta de acolhimento no topo do portal.">
        <Textarea value={description} onChange={(e) => setDescription(e.target.value)} rows={2}
          placeholder="Ex: Cuidado de verdade, no horário que cabe na sua rotina." maxLength={280} className="resize-none" />
      </Field>

      <Field label="Cor da marca" hint="Destaque do portal. Vazio = usa a cor do tema do ramo.">
        <div className="flex items-center gap-3">
          <input
            type="color"
            value={brandColor && HEX.test(brandColor) ? brandColor : "#4f46e5"}
            onChange={(e) => setBrandColor(e.target.value)}
            className="h-9 w-12 cursor-pointer rounded-md border border-border bg-transparent p-1"
            aria-label="Cor da marca"
          />
          <Input value={brandColor ?? ""} onChange={(e) => setBrandColor(e.target.value || null)}
            placeholder="#4f46e5" className="w-32 font-mono" />
          {brandColor && (
            <Button type="button" variant="ghost" size="sm" onClick={() => setBrandColor(null)}>
              Usar cor do tema
            </Button>
          )}
        </div>
        {!colorValid && <p className="text-xs text-destructive">Use um hex no formato #rrggbb.</p>}
      </Field>

      <div className="grid gap-4 sm:grid-cols-2">
        <Field label="WhatsApp" hint="Com DDD, só números.">
          <Input value={whatsApp} onChange={(e) => setWhatsApp(e.target.value)} type="tel" placeholder="Ex: 85999998888" maxLength={30} />
        </Field>
        <Field label="Endereço" hint="Exibido no rodapé do portal.">
          <Input value={address} onChange={(e) => setAddress(e.target.value)} placeholder="Rua, nº, bairro" maxLength={200} />
        </Field>
      </div>

      <div className="flex items-center gap-3">
        <Button onClick={save} disabled={mut.isPending || !colorValid}>
          {mut.isPending ? <><Loader2 className="mr-2 h-4 w-4 animate-spin" /> Salvando...</> : "Salvar identidade"}
        </Button>
        {mut.isSuccess && !mut.isPending && (
          <span className="flex items-center gap-1.5 text-sm text-primary"><Check className="h-4 w-4" /> Salvo</span>
        )}
      </div>
    </div>
  );
}

function Field({ label, hint, children }: { label: string; hint?: string; children: React.ReactNode }) {
  return (
    <div className="space-y-1.5">
      <label className="text-xs font-medium uppercase tracking-wide text-muted-foreground">{label}</label>
      {children}
      {hint && <p className="text-xs text-muted-foreground">{hint}</p>}
    </div>
  );
}
