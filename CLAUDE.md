# CLAUDE.md - Important Instructions

## Python Code Quality Standards

**CRITICAL**: ALWAYS maintain HIGH STANDARDS for Python code quality. DO NOT be lazy!

### Required Standards:
1. **Type Checking (mypy)**: MUST pass with NO errors
2. **Code Formatting (black)**: MUST be properly formatted
3. **Linting (ruff)**: MUST pass with NO errors
4. **Tests**: MUST maintain ≥55% coverage

### DO NOT:
- Leave type errors for "later" - FIX THEM NOW
- Ignore linting issues - FIX THEM NOW
- Accept "minor style improvements" - FIX THEM NOW
- Be lazy about code quality - MAINTAIN HIGH STANDARDS

### Always run before considering work complete:
```bash
cd python && ./verify-code-quality.sh
```

All checks MUST pass before moving on.