import type { ReactNode } from "react";

type PageIntroProps = {
  title: string;
  description: string;
  eyebrow?: string;
  actions?: ReactNode;
};

export default function PageIntro({
  title,
  description,
  eyebrow,
  actions,
}: PageIntroProps) {
  return (
    <header className="page-header">
      <div className="space-y-4">
        {eyebrow ? <div className="eyebrow">{eyebrow}</div> : null}
        <div className="space-y-3">
          <h1 className="page-title text-[var(--text)]">{title}</h1>
          <p className="page-copy max-w-3xl">{description}</p>
        </div>
      </div>
      {actions ? <div className="page-header-actions">{actions}</div> : null}
    </header>
  );
}
