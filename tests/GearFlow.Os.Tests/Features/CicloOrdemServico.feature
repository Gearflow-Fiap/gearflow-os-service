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
