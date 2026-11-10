import { useValue } from "cs2/api";
import { useLocalization } from "cs2/l10n";
import { taxEngineWarning$ } from "bindings";
import { engineWarningText } from "./engine-warning";
import styles from "./engine-warning-line.module.scss";

/** One line at the top of the Taxation tab when the tax engine isn't charging (or runs on an untested game version). */
export const EngineWarningLine = () => {
  const { translate } = useLocalization();
  const text = engineWarningText(useValue(taxEngineWarning$), translate);
  return text ? <div className={styles.warning}>{text}</div> : null;
};
