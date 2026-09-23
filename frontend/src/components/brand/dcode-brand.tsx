import { DCodeLogo } from "./dcode-logo";
import { DCodeWordmark } from "./dcode-wordmark";

export interface DCodeBrandProps {
  size?: number;
  className?: string;
  showWordmark?: boolean;
  showTagline?: boolean;
  showSupportingLine?: boolean;
}

export function DCodeBrand({
  size = 32,
  className,
  showWordmark = true,
  showTagline = false,
  showSupportingLine = false,
}: DCodeBrandProps) {
  return (
    <div className={["dcode-brand-lockup", className].filter(Boolean).join(" ")}>
      <DCodeLogo size={size} />
      {showWordmark || showTagline || showSupportingLine ? (
        <div className="dcode-brand-lockup__copy">
          {showWordmark ? <DCodeWordmark className="dcode-brand-lockup__wordmark" /> : null}
          {showTagline ? <span className="dcode-brand-lockup__tagline">Build software.</span> : null}
          {showSupportingLine ? <span className="dcode-brand-lockup__supporting">Your development workspace, from code to production.</span> : null}
        </div>
      ) : null}
    </div>
  );
}
