# Built on the main project's live-inference image: same Python, torch, models and AI code.
# Offline build: nothing is downloaded, only the small web app is added.
ARG BASE=fs-smartcctv-pep-demo-live:bundle-gpu
FROM ${BASE}
COPY mini /app/mini
EXPOSE 8090
CMD ["uvicorn", "mini.main:app", "--host", "0.0.0.0", "--port", "8090"]
