"""Export the trained local checkpoint for the Windows C# CPU login adapter."""
import argparse
import hashlib
import json
import sys
from pathlib import Path

def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--repository', type=Path, required=True)
    parser.add_argument('--checkpoint', type=Path, required=True)
    parser.add_argument('--output', type=Path, required=True)
    args = parser.parse_args()
    sys.path.insert(0, str(args.repository.resolve()))
    import torch
    from strhub.models.parseq.system import PARSeq
    torch.set_num_threads(4)
    torch.backends.mha.set_fastpath_enabled(False)
    model = PARSeq.load_from_checkpoint(str(args.checkpoint), map_location='cpu').float().eval()
    class Export(torch.nn.Module):
        def __init__(self):
            super().__init__()
            self.model = model
        def forward(self, images):
            return self.model.model(self.model.tokenizer, images, max_length=6)
    args.output.mkdir(parents=True, exist_ok=True)
    target = args.output / 'captcha.onnx'
    with torch.no_grad():
        torch.onnx.export(Export().eval(), torch.zeros(1, 3, 32, 128), str(target),
                          input_names=['images'], output_names=['logits'], opset_version=17,
                          do_constant_folding=True)
    # ORT 1.20 CPU has no boolean Where kernel. Preserve its exact boolean semantics.
    import onnx
    graph = onnx.shape_inference.infer_shapes(onnx.load(str(target)))
    types = {v.name: v.type.tensor_type.elem_type for v in graph.graph.value_info}
    replacements = []
    for node in graph.graph.node:
        if node.op_type == 'Where' and types.get(node.output[0]) == onnx.TensorProto.BOOL:
            cond, left, right = node.input
            name = node.name
            replacements.extend([
                onnx.helper.make_node('And', [cond,left], [name+'/left'], name=name+'/left'),
                onnx.helper.make_node('Not', [cond], [name+'/not'], name=name+'/not'),
                onnx.helper.make_node('And', [name+'/not',right], [name+'/right'], name=name+'/right'),
                onnx.helper.make_node('Or', [name+'/left',name+'/right'], list(node.output), name=name+'/or')])
        else:
            replacements.append(node)
    del graph.graph.node[:]
    graph.graph.node.extend(replacements)
    onnx.checker.check_model(graph)
    onnx.save(graph,str(target))
    metadata = dict(checkpoint_sha256=hashlib.sha256(args.checkpoint.read_bytes()).hexdigest(),
                    model_sha256=hashlib.sha256(target.read_bytes()).hexdigest(),
                    tokens=model.tokenizer._itos, input_size=[32,128], max_length=6,
                    preprocessing='RGB / Pillow bicubic / float32 / normalize 0.5',
                    source_repository_commit='1902db', inference='CPU; six ASCII alphanumeric only')
    (args.output / 'captcha.json').write_text(json.dumps(metadata, indent=2), encoding='utf-8')
    print(json.dumps(metadata))

if __name__ == '__main__':
    main()
