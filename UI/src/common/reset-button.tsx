import { Tooltip } from "cs2/ui";
import { CSSProperties, MouseEvent, ReactNode } from "react";
import { playSound } from "bindings";
import { SOUNDS } from "sounds";
import { TintedIcon } from "vanilla";
import { ResetKind, resetClickable, ResetVisibility } from "./slider-reset";
import styles from "./reset-button.module.scss";

// A slider row's reset button, shared by the Taxation tab and (later) the Services tab: vanilla's own reset glyph
// (the circular arrow the Options page resets a key binding with), tinted like vanilla's icon buttons. "city" adds a
// small city glyph in the middle: the row's own value is cleared so it follows the city. No inline SVG in Gameface,
// so both are masks over the text colour (vanilla TintedIcon, or a plain mask div should a patch move it).

export const RESET_GLYPH = "Media/Glyphs/ArrowCircular.svg";
export const CITY_GLYPH = "Media/Game/Icons/City.svg";

const Glyph = ({ src, className }: { src: string; className: string }) => {
  if (TintedIcon) return <TintedIcon src={src} className={className} />;
  return <div className={`${className} ${styles.mask}`} style={{ maskImage: `url(${src})` }} />;
};

export interface ResetButtonProps {
  kind: ResetKind;
  visibility: ResetVisibility;
  onReset: () => void;
  tooltip?: ReactNode;
  className?: string;
  style?: CSSProperties;
}

export const ResetButton = ({ kind, visibility, onReset, tooltip, className, style }: ResetButtonProps) => {
  const clickable = resetClickable(visibility);
  const onClick = (e: MouseEvent) => {
    // The slider column under it would take the click as a drag.
    e.stopPropagation();
    if (!clickable) return;
    playSound(SOUNDS.resetRow);
    onReset();
  };
  const button = (
    <div className={[styles.button, styles[visibility], className].filter(Boolean).join(" ")} style={style} onClick={onClick}>
      <Glyph src={RESET_GLYPH} className={styles.arrow} />
      {kind === "city" && <Glyph src={CITY_GLYPH} className={styles.city} />}
    </div>
  );
  return clickable && tooltip ? <Tooltip tooltip={tooltip}>{button}</Tooltip> : button;
};
