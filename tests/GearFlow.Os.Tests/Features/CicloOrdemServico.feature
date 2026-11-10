# language: pt
Funcionalidade: Ciclo orquestrado da ordem de serviço

  Cenário: Abrir a OS e concluir a Saga até a entrega
    Dado um cliente com um veículo cadastrado
    Quando a recepcionista abre uma OS com o serviço "Troca de óleo"
    Então o status da OS é "Created"
    E a consulta devolve o histórico com "Ordem de serviço aberta."
    Quando chega o evento BudgetGenerated
    Então o status da OS é "WaitingOrcamento"
    Quando chega o evento BudgetApproved
    Então o status da OS é "AwaitingPayment"
    Quando chega o evento PaymentConfirmed
    Então o status da OS é "InProgress"
    E o histórico contém o status "Paid"
    Quando chega o evento ExecutionFinished
    E a recepcionista registra a entrega
    Então o status da OS é "Completed"
    E o histórico contém o status "Completed"

  Cenário: Pagamento recusado cancela a OS e compensa as etapas anteriores
    Dado um cliente com um veículo cadastrado
    Quando a recepcionista abre uma OS com o serviço "Troca de pastilhas"
    E o diagnóstico encontra a peça "Pastilha de freio"
    E chega o evento BudgetGenerated
    E chega o evento BudgetApproved
    Então o status da OS é "AwaitingStock"
    Quando chega o evento StockReserved
    Então o status da OS é "AwaitingPayment"
    Quando chega o evento PaymentFailed
    Então o status da OS é "Cancelled"
    E a Saga enviou "ReleaseStock" para o Inventory
    E a Saga enviou "CancelOperation" para o Operation
