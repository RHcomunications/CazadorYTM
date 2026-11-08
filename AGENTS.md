# AGENTS.md — Directivas de Agentes y Arquitectura para Cazador YTM

Este documento define las directrices maestras de comportamiento, calidad, arquitectura y reglas operativas para todos los agentes de IA (Antigravity, Claude, Copilot, Cursor, etc.) que trabajen en el proyecto **Cazador YTM**.

---

## 1. Principios Fundamentales (de CLAUDE.md)

1. **Think Before Coding**:
   - Plantea suposiciones explícitamente antes de codificar. Si hay ambigüedad o múltiples enfoques, preséntalos; no decidas en silencio.
   - Si existe una solución más simple o quirúrgica, dilo y defiéndela.
   - Presenta diagnósticos y hojas de ruta para aprobación del usuario antes de aplicar cambios en código.

2. **Simplicity First**:
   - Escribe el código mínimo que resuelve el problema. Nada de características especulativas, abstracciones para un solo uso o sobreingeniería.

3. **Surgical Changes**:
   - Toca únicamente lo que debes tocar. No modifiques código adyacente ni formato no relacionado.
   - Mantén la integridad de comentarios y documentación existente.

4. **Goal-Driven Execution**:
   - Define criterios de éxito verificables (`dotnet test`, scripts de verificación) y valida antes de dar por terminada la tarea.

---

## 2. Invariantes Específicos del Proyecto Cazador YTM

1. **Internacionalización (i18n) Bilingüe Estricta**:
   - Paridad obligatoria al 100% entre `lang/es.json` y `lang/en.json`.
   - Cualquier nueva clave o texto en la UI debe agregarse simultáneamente en ambos idiomas.
   - Ejecutar siempre `scripts/Verify-I18n.ps1` tras modificar cadenas.

2. **Integridad de Pruebas Unitarias (235+ tests)**:
   - Toda suite de pruebas debe pasar al 100% sin advertencias ni omisiones (`dotnet test CazadorYTM.slnx -c Release`).
   - Todo cambio o nueva funcionalidad debe contar con pruebas en `CazadorYTM.Tests` o `CazadorYTM.Gui.Tests`.

3. **Accesibilidad Universal (WCAG 2.2 AA)**:
   - Todo elemento interactivo debe ser operable por teclado sin trampas ni fugas de foco (`KeyboardNavigation`).
   - Los controles deben contar con `AutomationProperties.Name` y `HelpText` localizados para compatibilidad total con lectores de pantalla (NVDA, JAWS y Narrador de Windows).

4. **Protocolo de Memoria Persistente (Engram)**:
   - Toda operación de memoria (`mem_save`, `mem_context`, `mem_search`, `mem_session_summary`) debe vincularse obligatoriamente al proyecto `project: "cazadorytm"`.
   - Registrar inmediatamente aprendizajes, correcciones de errores y decisiones arquitectónicas.

5. **Automatización de Lanzamientos y Versiones**:
   - Los lanzamientos semanales se efectúan mediante `scripts/New-WeeklyRelease.ps1`.
   - Las notas de versión deben redactarse de forma previa y detallada en `docs/releases/v<Version>.md`.
   - No crear etiquetas Git ni disparar flujos de compilación/release sin confirmación explícita del usuario.
