# AI service. Base = the AI image (Python 3.12, PyTorch 2.6 + CUDA 12.4, model weights and the
# APD v2 / vehicle violation code), so the detections are identical and the build is offline:
# only mini/ is added.
ARG BASE=fs-smartcctv-pep-demo-live:bundle-gpu
FROM ${BASE}
COPY mini /app/mini
EXPOSE 8090
CMD ["uvicorn", "mini.main:app", "--host", "0.0.0.0", "--port", "8090", "--no-access-log"]
