//pass
//--local_size=64 --num_groups=64 --no-inline

__kernel void foo(__local int* A, int i) {
  A[i] = get_local_id(0) / 64;
}
