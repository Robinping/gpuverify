#!/bin/bash
set -ev

if [ -z ${DOWNLOADS_DIR+x} ]; then
  echo "DOWNLOADS_DIR not set"
  exit 1
fi

if [ -z ${LLVM_VERSION+x} ]; then
  echo "LLVM_VERSION not set"
  exit 1
fi

if [ "${GPUVERIFY_DIR}" == "${BUILD_ROOT}" ]; then
  ${GPUVERIFY_DIR}/.travis/build_gpuverify.sh
  ${GPUVERIFY_DIR}/.travis/build_bugle.sh
elif [ "${BUGLE_DIR}" == "${BUILD_ROOT}" ]; then
  ${GPUVERIFY_DIR}/.travis/build_bugle.sh
  ${GPUVERIFY_DIR}/.travis/build_gpuverify.sh
else
  echo "Unexpected BUILD_ROOT: ${BUILD_ROOT}"
  exit 1
fi

${GPUVERIFY_DIR}/.travis/clone_build_and_install_libclc.sh

if [ "${DEFAULT_SOLVER}" == "z3" ]; then
  if [ -z ${Z3+x} ]; then
    echo "Z3 not set"
    exit 1
  fi
  ${GPUVERIFY_DIR}/.travis/download_and_symlink_z3.sh
elif [ "${DEFAULT_SOLVER}" == "cvc4" ]; then
  if [ -z ${CVC4+x} ]; then
    echo "CVC4 not set"
    exit 1
  fi
  ${GPUVERIFY_DIR}/.travis/download_and_symlink_cvc4.sh
else
  echo "Unknown default solver: ${DEFAULT_SOLVER}"
  exit 1
fi

${GPUVERIFY_DIR}/.travis/symlink_llvm_tools.sh
${GPUVERIFY_DIR}/.travis/test_gpuverify.sh
